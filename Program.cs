using System.Net;
using System.Net.Sockets;
using System.Text;

class Server
{
    private static Dictionary<string, TcpClient> clients = new Dictionary<string, TcpClient>();
    private static TcpListener listener;

    static async Task Main()
    {
        listener = new TcpListener(IPAddress.Any, 5000);
        listener.Start();
        Console.WriteLine("Сервер запущен...");

        while (true)
        {
            TcpClient client = await listener.AcceptTcpClientAsync();
            _ = HandleClientAsync(client);
        }
    }

    private static async Task HandleClientAsync(TcpClient client)
    {
        NetworkStream stream = client.GetStream();
        byte[] buffer = new byte[1024];
        int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
        string login = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();

        Console.WriteLine("Login: " + login);
        foreach(var user in clients)
        {
            Console.WriteLine(user.Key + " " + user.Value.ToString());
        }

        if (!clients.ContainsKey(login))
        {
            clients.Add(login, client);
            Console.WriteLine($"{login} подключился.");

            byte[] response = Encoding.UTF8.GetBytes("OK");
            await stream.WriteAsync(response, 0, response.Length);
        }
        else
        {
            byte[] response = Encoding.UTF8.GetBytes("ERROR");
            await stream.WriteAsync(response, 0, response.Length);
            client.Close();
            return;
        }

        try
        {
            while (true)
            {
                bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead == 0) break;

                string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                string[] parts = message.Split(':', 2);
                if (parts.Length < 2) continue;

                string recipient = parts[0];
                string text = parts[1];

                if (clients.ContainsKey(recipient))
                {
                    NetworkStream recipientStream = clients[recipient].GetStream();
                    byte[] data = Encoding.UTF8.GetBytes($"{login}: {text}");
                    await recipientStream.WriteAsync(data, 0, data.Length);
                    Console.WriteLine(text +  " Recieved from: " + login + " to: " + clients[recipient]);
                }
            }
        }
        catch { }

        clients.Remove(login);
        Console.WriteLine($"{login} отключился.");
        client.Close();
    }

}
