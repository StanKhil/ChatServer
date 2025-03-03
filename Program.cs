using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

class Server
{
    private static Dictionary<string, TcpClient> clients = new Dictionary<string, TcpClient>();
    private static List<string> connectedUsers = new List<string>();
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

    /*private static async Task AddUser(string login)
    {
        connectedUsers.Add(login);
        foreach (var client in clients)
        {
            NetworkStream stream = client.Value.GetStream();
            await stream.WriteAsync(Encoding.UTF8.GetBytes($"ADD:{login.Trim()}"));
            await stream.FlushAsync();
        }
    }


    private static async Task RemoveUser(string login)
    {
        connectedUsers.Remove(login);
        foreach (var client in clients)
        {
            NetworkStream stream = client.Value.GetStream();
            await stream.WriteAsync(Encoding.UTF8.GetBytes($"REMOVE:{login.Trim()}"));
            await stream.FlushAsync();
        }
    }*/




    private static async Task Broadcast(string message)
    {
        byte[] data = Encoding.UTF8.GetBytes(message + "\n");
        Console.WriteLine($"[SERVER] Отправлено сообщение: {message}");
        List<string> disconnectedUsers = new List<string>();

        foreach (var user in clients)
        {
            try
            {
                NetworkStream stream = user.Value.GetStream();
                await stream.WriteAsync(data, 0, data.Length);
                await stream.FlushAsync();
            }
            catch
            {
                disconnectedUsers.Add(user.Key);
            }
        }

        foreach (string user in disconnectedUsers)
        {
            clients.Remove(user);
            Console.WriteLine($"Отключен: {user}");
        }
    }

    private static async Task HandleClientAsync(TcpClient client)
    {
        NetworkStream stream = client.GetStream();
        byte[] buffer = new byte[1024];
        int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
        string login = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();

        if (string.IsNullOrWhiteSpace(login) || clients.ContainsKey(login))
        {
            await stream.WriteAsync(Encoding.UTF8.GetBytes("ERROR\n"));
            client.Close();
            return;
        }

        clients[login] = client;
        Console.WriteLine($"{login} подключился.");
        await stream.WriteAsync(Encoding.UTF8.GetBytes("OK\n"));
        await stream.FlushAsync();

        string userList = string.Join(",", clients.Keys);
        await Broadcast($"USERS:{userList}");
        await Broadcast($"ADD:{login}");

        try
        {
            while (true)
            {
                bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead == 0) break;

                string message = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
                string[] parts = message.Split(':', 2);

                if (parts.Length < 2) continue;
                string recipient = parts[0];
                string content = parts[1];

                if (content.StartsWith("FILE:"))
                {
                    await ReceiveFile(stream, recipient, login, message);
                }
                else
                { 
                    if (clients.ContainsKey(recipient))
                    {
                        NetworkStream recipientStream = clients[recipient].GetStream();
                        await recipientStream.WriteAsync(Encoding.UTF8.GetBytes($"{login}: {content}\n"));
                        await recipientStream.FlushAsync();
                    }
                }
            }
        }
        catch { }

        clients.Remove(login);
        Console.WriteLine($"{login} отключился.");
        await Broadcast($"REMOVE:{login}");
        client.Close();
    }

    private static async Task ReceiveFile(NetworkStream stream, string recipient, string sender, string message)
    {
        try
        {
            byte[] buffer = new byte[4096];

            Console.WriteLine($"Получен заголовок: {message}");

            string[] headerParts = message.Split(':', StringSplitOptions.RemoveEmptyEntries);
            /*if (headerParts.Length < 3 || !headerParts[1].Trim().Equals("FILE", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Ошибка: некорректный заголовок файла!");
                await stream.WriteAsync(Encoding.UTF8.GetBytes("ERROR\n"));
                return;
            }*/

            string fileNameSize = headerParts[2].Trim();
            int lastSpaceIndex = fileNameSize.LastIndexOf(' ');

            /*if (lastSpaceIndex == -1)
            {
                Console.WriteLine("Ошибка: заголовок не содержит размер файла!");
                await stream.WriteAsync(Encoding.UTF8.GetBytes("ERROR\n"));
                return;
            }*/

            string fileName = fileNameSize.Substring(0, lastSpaceIndex);
            string fileSizeStr = fileNameSize.Substring(lastSpaceIndex + 1);

            /*if (!long.TryParse(fileSizeStr, out long fileSize))
            {
                Console.WriteLine($"Ошибка: неверный размер файла! File {fileName} size {fileSizeStr}");
                await stream.WriteAsync(Encoding.UTF8.GetBytes("ERROR\n"));
                return;
            }*/

            await stream.WriteAsync(Encoding.UTF8.GetBytes("OK\n"));
            long fileSize = long.Parse(fileSizeStr);
            string filePath = Path.Combine("ReceivedFiles", fileName);
            Directory.CreateDirectory("ReceivedFiles");

            Console.WriteLine($"Начало получения файла {fileName} от {sender} (Размер: {fileSize} байт)");

            using (FileStream fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            {
                long totalBytesRead = 0;
                int bytesRead;

                while (totalBytesRead < fileSize)
                {
                    bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    totalBytesRead += bytesRead;

                    Console.WriteLine($"Получено {totalBytesRead}/{fileSize} байт...");
                }

                Console.WriteLine($"Файл {fileName} ({fileSize} байт) получен от {sender}");
            }

            if (clients.ContainsKey(recipient))
            {
                NetworkStream recipientStream = clients[recipient].GetStream();
                await recipientStream.WriteAsync(Encoding.UTF8.GetBytes($"FILE:{sender}|{fileName}\n"));

                using (FileStream sendFileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    int bytesRead;
                    while ((bytesRead = await sendFileStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await recipientStream.WriteAsync(buffer, 0, bytesRead);
                    }

                    Console.WriteLine($"Файл {fileName} отправлен пользователю {recipient}");
                }
            }
            else
            {
                Console.WriteLine($"Пользователь {recipient} не в сети, файл сохранён на сервере.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка при получении файла: {ex.Message}");
        }
    }

}

