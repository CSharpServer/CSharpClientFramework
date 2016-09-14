using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using System.Threading;
using System.Net;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Diagnostics;
using CSharpServerFramework.Util;

namespace CSharpClientFramework
{
    public class CSharpServerClientBase : IDisposable
    {
        protected TcpClient Client { get; set; }
        private byte[] receiveBuffer = new byte[16 * 1024];
        private volatile bool _isRunning = false;
        protected EventHandlerList Events;
        private readonly int TCP_PACKAGE_HEAD_SIZE = 4;

        protected IDictionary<string, object> HandlerKeyMap { get; private set; }

        public event EventHandler<CSharpServerClientEventArgs> OnConnected;
        public event EventHandler<CSharpServerClientEventArgs> OnDisconnected;
        public event EventHandler<CSharpServerClientEventArgs> OnSendFailed;
        public event EventHandler<CSharpServerClientReceiveMessageEventArgs> OnMessageReceived;
        public IDeserializeMessage MessageDepressor { get; set; }

        public CSharpServerClientBase(IDeserializeMessage MessageDepressor)
        {
            this.MessageDepressor = MessageDepressor;
            Init();
        }

        private void Init()
        {
            Events = new EventHandlerList();
            HandlerKeyMap = new Dictionary<string, object>();
        }

        public void SetBufferSize(int NewLength)
        {
            receiveBuffer = new byte[NewLength];
        }

        public void AddHandlerCallback(string ExtensionName, object Command, EventHandler<CSharpServerClientEventArgs> Callback)
        {
            object key = GenerateKey(ExtensionName, Command);
            Events.AddHandler(key, Callback);
        }

        private object GenerateKey(string ExtensionName, object Command)
        {
            var cmd = GenerateCmdValue(Command);
            string key = GenerateCmdKey(ExtensionName, cmd);
            if (HandlerKeyMap.ContainsKey(key))
            {
                return HandlerKeyMap[key];
            }
            else
            {
                HandlerKeyMap[key] = key;
            }
            return key;
        }

        private string GenerateCmdKey(string ExtensionName, string cmdValue)
        {
            string key = string.Format("On{0}_{1}", ExtensionName, cmdValue);
            return key;
        }

        private string GenerateCmdValue(object Command)
        {
            string cmd = null;
            if (Command is int)
            {
                cmd = string.Format("CmdId({0})", Command);
            }
            else
            {
                cmd = Command.ToString();
            }
            return cmd;
        }

        protected bool IsRunning
        {
            get { return _isRunning; }
            private set { _isRunning = value; }
        }


        public void Start(IPAddress Ip, int Port)
        {

            _isRunning = false;
            Task.Run(async () =>
            {
                Client = new TcpClient();
                try
                {
                    await Client.ConnectAsync(Ip, Port);
                    IsRunning = true;
                    ReceiveHead();
                    DispatchClientConnected();
                }
                catch (Exception ex)
                {
                    DispatchSendFailed(ex, "Remote Server Not Response");
                    DispatchClientDisconnected();
                }
            });
        }


        protected void DispatcherEvent(EventHandler<CSharpServerClientEventArgs> handler, CSharpServerClientEventArgs args)
        {
            if (handler == null) return;
            object[] param = new object[] { this, args };
            EventDispatcherUtil.AsyncDispatcherEvent(handler, this, args);
        }

        public void SendMessageAsync(byte[] Data, int Length)
        {
            Task.Run(async () =>
            {
                byte[] sendData = new byte[Data.Length + 4];
                int len = BitUtil.CreateDataPackageWithHead(sendData, Data, Length);
                try
                {
                    var buf = new ArraySegment<byte>(sendData, 0, len);
                    var sended = await Client.Client.SendAsync(buf, SocketFlags.None);
                }
                catch (Exception ex)
                {
                    DispatchSendFailed(ex, "Send Message Failed");
                }
            });

        }


        public void SendMessage(byte[] Data, int Length)
        {
            byte[] sendData = new byte[Length + 4];
            int len = BitUtil.CreateDataPackageWithHead(sendData, Data, Length);
            try
            {
                Client.Client.Send(sendData, 0, len, SocketFlags.None);
            }
            catch (Exception ex)
            {
                DispatchSendFailed(ex, "Send Message Failed");
            }

        }

        private void ReceiveHead()
        {
            Task.Run(async () =>
            {
                try
                {
                    var buf = new ArraySegment<byte>(receiveBuffer, 0, 4);
                    var received = await Client.Client.ReceiveAsync(buf, SocketFlags.None);
                    DoReveivePackageHead(received);
                }
                catch (Exception ex)
                {
#if DEBUG
                    Console.WriteLine("Receive Head Exception:{0}", ex.Message);
#endif
                }
            });

        }

        private void DoReveivePackageHead(int received)
        {
            int len = received;
            if (TCP_PACKAGE_HEAD_SIZE == len)
            {
                int packLen = BitConverter.ToInt32(receiveBuffer, 0);
                ///开始接收实际数据包
                ReceiveData(packLen);
            }
            else
            {
#if DEBUG
                Console.WriteLine("Receive Bad Head Size:{0}", len);
#endif
            }
        }

        private void ReceiveData(int PackageLength)
        {
            Task.Run(async () =>
            {
                var buf = new ArraySegment<byte>(receiveBuffer, 0, PackageLength);
                try
                {
                    var received = await Client.Client.ReceiveAsync(buf, SocketFlags.None);
                    DoClientLoopReceiveCallback(received, PackageLength);
                }
                catch (Exception ex)
                {
#if DEBUG
                    Console.WriteLine("Receive Data Exception:{0}", ex.Message);
#endif
                }
            });
        }

        private void DoClientLoopReceiveCallback(int received, int packLen)
        {
            var receiveBufferCopy = new byte[received];
            Array.Copy(receiveBuffer, receiveBufferCopy, received);
            ReceiveHead();

            if (received == packLen)
            {
                try
                {
                    
                    CSharpServerClientBaseMessage msg = MessageDepressor.GetMessageFromBuffer(receiveBufferCopy, received);
                    CSharpServerClientEventArgs args = new CSharpServerClientEventArgs();
                    args.State = msg;
                    object handlerKey = null;
                    if (string.IsNullOrWhiteSpace(msg.CmdName))
                    {
                        handlerKey = GenerateKey(msg.ExtName, msg.CmdId);
                    }
                    else
                    {
                        handlerKey = GenerateKey(msg.ExtName, msg.CmdName);
                    }
                    object eventHandler = Events[handlerKey];
                    EventHandler<CSharpServerClientEventArgs> handler = eventHandler as EventHandler<CSharpServerClientEventArgs>;
                    if (handler != null)
                    {
                        DispatcherEvent(handler, args);
                    }
                    EventDispatcherUtil.AsyncDispatcherEvent(OnMessageReceived, this,
                    new CSharpServerClientReceiveMessageEventArgs()
                    {
                        ReceiveMessage = receiveBufferCopy,
                        Client = this
                    });
                }
                catch (Exception ex)
                {
#if DEBUG
                    Console.WriteLine("MessageDepressor:{0}", ex.Message);
#endif
                }
                
            }
            else
            {
#if DEBUG
                Console.WriteLine("Received Data Invalid Length:{0}, Expected:{1}", received, packLen);
#endif
            }
        }

        private void DispatchClientConnected()
        {
            if (OnConnected != null)
            {
                EventDispatcherUtil.DispatcherEvent(this.OnConnected, this, new CSharpServerClientEventArgs
                {
                    Client = this
                });
            }
        }

        private void DispatchClientDisconnected()
        {
            if(OnDisconnected != null)
            {
                EventDispatcherUtil.DispatcherEvent(this.OnDisconnected, this, new CSharpServerClientEventArgs
                {
                    Client = this
                });
            }
        }

        private void DispatchSendFailed(Exception ex, string Message = null)
        {
            object[] param = new object[]
            {
                this,
                new CSharpServerClientEventArgs
                {
                    State = ex,
                    Client = this
                }
            };
            var handler = Events["OnSendFailed"];
            if (this.OnSendFailed != null)
            {
                EventDispatcherUtil.DispatcherEvent(this.OnSendFailed, this, new CSharpServerClientEventArgs { Client = this });
            }
        }


        public void Close()
        {
            if (IsRunning && Client != null && Client.Connected)
            {
                IsRunning = false;
                DispatchClientDisconnected();
                Client.Dispose();
            }
        }


        #region IDisposable 成员


        public void Dispose()
        {
            Close();
            Events.Dispose();
        }



        #endregion
    }

    public interface IDeserializeMessage
    {
        CSharpServerClientBaseMessage GetMessageFromBuffer(byte[] receiveBuffer, int len);
    }

    public class CSharpServerClientBaseMessage
    {
        public string ExtName { get; set; }
        public int CmdId { get; set; }
        public string CmdName{ get; set; }
    }

}
