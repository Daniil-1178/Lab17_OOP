using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows.Forms;
using System.Threading.Tasks;

namespace Lab17
{
    public partial class Form1 : Form
    {
        private static TcpListener tcpListener;
        private List<ServerClientObject> serverClients = new List<ServerClientObject>();
        private bool isServerRunning = false;

        private TcpClient client;
        private NetworkStream stream;
        private bool isClientConnected = false;
        private string userName;

        public Form1()
        {
            InitializeComponent();

            loginButton.Enabled = true;
            logoutButton.Enabled = false;
            sendButton.Enabled = false;
        }

        protected internal void AppendServerLog(string message)
        {
            if (txtServerLog.InvokeRequired)
            {
                txtServerLog.Invoke(new Action<string>(AppendServerLog), message);
                return;
            }
            txtServerLog.AppendText($"[{DateTime.Now.ToShortTimeString()}] {message}\r\n");
        }

        protected internal void AppendChat(string message)
        {
            if (chatTextBox.InvokeRequired)
            {
                chatTextBox.Invoke(new Action<string>(AppendChat), message);
                return;
            }
            chatTextBox.AppendText(message + "\r\n");
        }

        private void btnStartServer_Click(object sender, EventArgs e)
        {
            if (isServerRunning) return;

            try
            {
                tcpListener = new TcpListener(IPAddress.Any, 8888);
                tcpListener.Start();
                isServerRunning = true;
                btnStartServer.Enabled = false;
                AppendServerLog("Сервер запущено. Очікування підключень...");

                Task.Run(() => ListenForClients());
            }
            catch (Exception ex)
            {
                MessageBox.Show("Помилка запуску сервера: " + ex.Message);
            }
        }

        private void ListenForClients()
        {
            try
            {
                while (isServerRunning)
                {
                    TcpClient tcpClient = tcpListener.AcceptTcpClient();
                    ServerClientObject clientObject = new ServerClientObject(tcpClient, this);
                    Task.Run(() => clientObject.Process());
                }
            }
            catch
            {

            }
        }

        protected internal void AddConnection(ServerClientObject clientObject)
        {
            serverClients.Add(clientObject);
        }

        protected internal void RemoveConnection(string id)
        {
            ServerClientObject client = serverClients.FirstOrDefault(c => c.Id == id);
            if (client != null) serverClients.Remove(client);
        }

        protected internal void BroadcastMessage(string message, string id)
        {
            byte[] data = Encoding.Unicode.GetBytes(message);
            for (int i = 0; i < serverClients.Count; i++)
            {
                if (serverClients[i].Id != id)
                {
                    try
                    {
                        serverClients[i].Stream.Write(data, 0, data.Length);
                    }
                    catch
                    {

                    }
                }
            }
        }

        private void DisconnectServer()
        {
            isServerRunning = false;
            if (tcpListener != null) tcpListener.Stop();

            for (int i = 0; i < serverClients.Count; i++)
            {
                serverClients[i].Close();
            }
            serverClients.Clear();
        }

        private void loginButton_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(userNameTextBox.Text))
            {
                MessageBox.Show("Введіть своє ім'я!");
                return;
            }

            userName = userNameTextBox.Text.Trim();
            client = new TcpClient();

            try
            {
                client.Connect("127.0.0.1", 8888);
                stream = client.GetStream();

                byte[] data = Encoding.Unicode.GetBytes(userName);
                stream.Write(data, 0, data.Length);

                isClientConnected = true;
                loginButton.Enabled = false;
                logoutButton.Enabled = true;
                sendButton.Enabled = true;
                userNameTextBox.ReadOnly = true;

                AppendChat($"Ласкаво просимо, {userName}!");

                Task.Run(() => ReceiveMessages());
            }
            catch (Exception ex)
            {
                MessageBox.Show("Помилка підключення до сервера: " + ex.Message);
            }
        }

        private void ReceiveMessages()
        {
            try
            {
                while (isClientConnected)
                {
                    byte[] data = new byte[64];
                    StringBuilder builder = new StringBuilder();
                    int bytes = 0;

                    do
                    {
                        bytes = stream.Read(data, 0, data.Length);
                        if (bytes == 0)
                        {
                            DisconnectClient();
                            return;
                        }
                        builder.Append(Encoding.Unicode.GetString(data, 0, bytes));
                    }
                    while (stream.DataAvailable);

                    string message = builder.ToString();
                    AppendChat(message);
                }
            }
            catch
            {
                if (isClientConnected)
                {
                    AppendChat("Підключення перервано!");
                    DisconnectClient();
                }
            }
        }

        private void sendButton_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(messageTextBox.Text) || !isClientConnected) return;

            try
            {
                string message = messageTextBox.Text;
                byte[] data = Encoding.Unicode.GetBytes(message);
                stream.Write(data, 0, data.Length);

                AppendChat($"{userName}: {message}");
                messageTextBox.Clear();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Помилка відправки повідомлення: " + ex.Message);
            }
        }

        private void logoutButton_Click(object sender, EventArgs e)
        {
            DisconnectClient();
        }

        private void DisconnectClient()
        {
            if (!isClientConnected) return;

            isClientConnected = false;
            if (stream != null) stream.Close();
            if (client != null) client.Close();

            if (this.IsHandleCreated)
            {
                this.Invoke(new MethodInvoker(() =>
                {
                    loginButton.Enabled = true;
                    logoutButton.Enabled = false;
                    sendButton.Enabled = false;
                    userNameTextBox.ReadOnly = false;
                }));
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            DisconnectClient();
            DisconnectServer();
        }
    }

    public class ServerClientObject
    {
        protected internal string Id { get; private set; }
        protected internal NetworkStream Stream { get; private set; }
        private string userName;
        private TcpClient client;
        private Form1 serverForm;

        public ServerClientObject(TcpClient tcpClient, Form1 form)
        {
            Id = Guid.NewGuid().ToString();
            client = tcpClient;
            serverForm = form;
            serverForm.AddConnection(this);
        }

        public void Process()
        {
            try
            {
                Stream = client.GetStream();
                userName = GetMessage();
                if (string.IsNullOrEmpty(userName)) return;

                string message = userName + " увійшов в чат";
                serverForm.BroadcastMessage(message, this.Id);
                serverForm.AppendServerLog(message);

                while (true)
                {
                    message = GetMessage();
                    if (string.IsNullOrEmpty(message))
                    {
                        message = userName + " покинув чат";
                        serverForm.AppendServerLog(message);
                        serverForm.BroadcastMessage(message, this.Id);
                        break;
                    }

                    message = string.Format("{0}: {1}", userName, message);
                    serverForm.AppendServerLog(message);
                    serverForm.BroadcastMessage(message, this.Id);
                }
            }
            catch
            {
                string message = userName + " покинув чат";
                serverForm.AppendServerLog(message);
                serverForm.BroadcastMessage(message, this.Id);
            }
            finally
            {
                serverForm.RemoveConnection(this.Id);
                Close();
            }
        }

        private string GetMessage()
        {
            try
            {
                byte[] data = new byte[64];
                StringBuilder builder = new StringBuilder();
                int bytes = 0;

                do
                {
                    bytes = Stream.Read(data, 0, data.Length);
                    if (bytes == 0) return null;
                    builder.Append(Encoding.Unicode.GetString(data, 0, bytes));
                }
                while (Stream.DataAvailable);

                return builder.ToString();
            }
            catch
            {
                return null;
            }
        }

        protected internal void Close()
        {
            if (Stream != null) Stream.Close();
            if (client != null) client.Close();
        }
    }
}