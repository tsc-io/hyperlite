namespace Hyperlite.Explorer
{
    using System;
    using System.Collections.Generic;

    internal delegate void NotifyConnectionStateChangedEventHandler(object sender, NotifyConnectionStateChangedEventArgs e);

    internal enum ConnectionState
    {
        Disconnected,

        Disconnecting,

        Connecting,

        Connected
    }

    internal sealed class NotifyConnectionStateChangedEventArgs : EventArgs
    {
        public NotifyConnectionStateChangedEventArgs(ConnectionState connectionState)
        {
            this.ConnectionState = connectionState;
        }

        public NotifyConnectionStateChangedEventArgs(ConnectionState connectionState, string connectionString)
        {
            this.ConnectionState = connectionState;
            this.ConnectionString = connectionString;
        }

        public ConnectionState ConnectionState { get; private set; }

        public string ConnectionString { get; private set; }
    }

    internal sealed class DatabaseSession : IDisposable
    {
        private static readonly object SyncRoot = new object();

        private IClient client;

        private IContext context;

        private DatabaseSession(string connectionString)
        {
            this.context = Context.Create(connectionString + ";ConnectionTimeout=15000");
            this.client = this.context.CreateClient();
        }

        public static event NotifyConnectionStateChangedEventHandler ConnectionStateChanged;

        public static DatabaseSession Instance { get; private set; }

        public static void Connect(string connectionString)
        {
            lock (SyncRoot)
            {
                Disconnect();
                FireConnectionStateChanged(new NotifyConnectionStateChangedEventArgs(ConnectionState.Connecting, connectionString));
                Instance = new DatabaseSession(connectionString);
                FireConnectionStateChanged(new NotifyConnectionStateChangedEventArgs(ConnectionState.Connected, connectionString));
            }
        }

        public static void Disconnect()
        {
            DatabaseScanner.Cancel();

            lock (SyncRoot)
            {
                if (Instance != null)
                {
                    FireConnectionStateChanged(new NotifyConnectionStateChangedEventArgs(ConnectionState.Disconnecting));
                    Instance.Close();
                    Instance = null;
                    FireConnectionStateChanged(new NotifyConnectionStateChangedEventArgs(ConnectionState.Disconnected));
                }
            }
        }

        public void BeginScan(string namespaceName, string tableName, Action<IList<Cell>> action)
        {
            if (this.client == null)
            {
                throw new InvalidOperationException("Session has been closed");
            }

            DatabaseScanner.BeginScan(this.client, namespaceName, tableName, action);
        }

        public void Close()
        {
            if (this.client != null)
            {
                this.client.Dispose();
                this.client = null;
            }

            if (this.context != null)
            {
                this.context.Dispose();
                this.context = null;
            }
        }

        public void Dispose()
        {
            this.Close();
        }

        public Cell Find(string namespaceName, string tableName, Key key)
        {
            if (this.client == null)
            {
                throw new InvalidOperationException("Session has been closed");
            }

            return DatabaseScanner.Find(this.client, namespaceName, tableName, key);
        }

        public DatabaseDirectoryInfo GetDirectoryInfo(string name)
        {
            if (this.client == null)
            {
                throw new InvalidOperationException("Session has been closed");
            }

            using (var ns = this.client.OpenNamespace(name))
            {
                return new DatabaseDirectoryInfo(ns.GetListing());
            }
        }

        private static void FireConnectionStateChanged(NotifyConnectionStateChangedEventArgs e)
        {
            if (ConnectionStateChanged != null)
            {
                ConnectionStateChanged(null, e);
            }
        }
    }
}
