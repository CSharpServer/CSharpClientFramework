using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CSharpClientFramework
{
    public class CSharpServerClientEventArgs :EventArgs
    {
        /// <summary>
        /// Event State
        /// </summary>
        public object State { get; set; }

        /// <summary>
        /// Client
        /// </summary>
        public CSharpServerClientBase Client { get; set; }
    }

    public class CSharpServerClientReceiveMessageEventArgs : EventArgs
    {
        /// <summary>
        /// Event State
        /// </summary>
        public byte[] ReceiveMessage { get; set; }

        /// <summary>
        /// Client
        /// </summary>
        public CSharpServerClientBase Client { get; set; }
    }
}
