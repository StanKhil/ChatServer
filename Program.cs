using System.Net;
using System.Net.Sockets;
using System.Text;

class Server
{
    private static Dictionary<string, TcpClient> clients = new Dictionary<string, TcpClient>();
    private static List<string> connectedUsers = new List<string>();
    private static TcpListener listener;
    private static List<string> groups = new List<string>();
    private static Dictionary<string, List<string>> usersInGroup= new Dictionary<string, List<string>>();

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


    private static async Task Broadcast(string message)
    {
        byte[] data = Encoding.UTF8.GetBytes(message + "\n");
        Console.WriteLine($"[SERVER] Отправлено сообщение: {message}");

        foreach (var user in clients)
        {
            try
            {
                NetworkStream stream = user.Value.GetStream();
                await stream.WriteAsync(data, 0, data.Length);
                await stream.FlushAsync();
            }
            catch { }
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

        await Broadcast($"USERS:{string.Join(",", clients.Keys)}");
        await Broadcast($"ADD:{login}");

        try
        {
            while (true)
            {
                bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead == 0) break;

                string message = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
                string[] parts = message.Split(':', 4);

                if (parts.Length < 2) continue;

                string command = parts[0];
                string recipient = parts[1];
                string content = string.Join(":", parts.Skip(2));

                if (command == "FILE")
                {
                    // Обработка файлов
                    if (recipient == "GROUP")
                    {
                        string groupName = parts[2];
                        string fileName = parts[3];
                        string fileSize = parts.Length > 4 ? parts[4] : "0";
                        await ReceiveFile(stream, groupName, login, message);
                    }
                    else
                    {
                        string fileName = parts[2];
                        string fileSize = parts.Length > 3 ? parts[3] : "0";
                        await ReceiveFile(stream, recipient, login, message);
                    }
                }
                else if (command == "MESSAGE")
                {
                    if (recipient == "GROUP")
                    {
                        string groupName = parts[2];
                        string groupMessage = parts.Length > 3 ? parts[3] : string.Empty;
                        await SendMessageToGroup(groupName, login, groupMessage);
                    }
                    else if (clients.ContainsKey(recipient))
                    {
                        NetworkStream recipientStream = clients[recipient].GetStream();
                        await recipientStream.WriteAsync(Encoding.UTF8.GetBytes($"{login}: {content}\n"));
                        await recipientStream.FlushAsync();
                    }
                }
                else if (command == "ADDGROUP")
                {
                    string groupName = parts[1];
                    await Broadcast($"ADDGROUP:{groupName}");
                }
                else if (command == "UPDATEGROUP")
                {
                    string groupName = parts[1];
                    string groupUsers = parts[2];
                    await Broadcast($"UPDATEGROUP:{groupName}:{groupUsers}");
                }
            }
        }
        catch { }

        clients.Remove(login);
        Console.WriteLine($"{login} отключился.");
        await Broadcast($"REMOVE:{login}");
        client.Close();
    }


    private static async Task SendMessageToGroup(string groupName, string sender, string message)
    {
        if (!usersInGroup.ContainsKey(groupName) || !usersInGroup[groupName].Contains(sender))
        {
            Console.WriteLine($"Ошибка: {sender} не в группе {groupName}!");
            return;
        }

        string fullMessage = $"GROUP:{groupName}:{sender}: {message}\n";
        byte[] data = Encoding.UTF8.GetBytes(fullMessage);

        foreach (string user in usersInGroup[groupName])
        {
            if (clients.ContainsKey(user))
            {
                NetworkStream stream = clients[user].GetStream();
                await stream.WriteAsync(data, 0, data.Length);
                await stream.FlushAsync();
            }
        }
    }

    private static async Task ReceiveFile(NetworkStream stream, string recipient, string sender, string message)
    {
        try
        {
            byte[] buffer = new byte[4096];

            Console.WriteLine($"Получен заголовок: {message}");

            string[] headerParts = message.Split(':', 5, StringSplitOptions.RemoveEmptyEntries);
            if (headerParts.Length < 4 || !headerParts[0].Trim().Equals("FILE", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Ошибка: некорректный заголовок файла!");
                return;
            }

            string target = headerParts[1].Trim();  // Либо имя пользователя, либо "GROUP"
            string groupName = target == "GROUP" ? headerParts[2].Trim() : "";
            string fileName = headerParts[target == "GROUP" ? 3 : 2].Trim();
            string fileSizeStr = headerParts[target == "GROUP" ? 4 : 3].Trim();

            if (!long.TryParse(fileSizeStr, out long fileSize))
            {
                Console.WriteLine($"Ошибка: неверный размер файла! File {fileName} size {fileSizeStr}");
                return;
            }

            //await stream.WriteAsync(Encoding.UTF8.GetBytes("OK\n"));
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

            if (target == "GROUP" && usersInGroup.ContainsKey(groupName))
            {
                Console.WriteLine($"Файл {fileName} предназначен для группы {groupName}");
                foreach (string user in usersInGroup[groupName])
                {
                    if (clients.ContainsKey(user) && user != sender)
                    {
                        NetworkStream recipientStream = clients[user].GetStream();
                        await recipientStream.WriteAsync(Encoding.UTF8.GetBytes($"GROUPFILE:{groupName}:{sender}|{fileName}\n"));

                        using (FileStream sendFileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            int bytesRead;
                            while ((bytesRead = await sendFileStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                await recipientStream.WriteAsync(buffer, 0, bytesRead);
                            }
                        }

                        Console.WriteLine($"Файл {fileName} отправлен пользователю {user} из группы {groupName}");
                    }
                }
            }
            else if (clients.ContainsKey(recipient))
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
                }

                Console.WriteLine($"Файл {fileName} отправлен пользователю {recipient}");
            }
            else
            {
                Console.WriteLine($"Пользователь или группа {recipient} не в сети, файл сохранён на сервере.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка при получении файла: {ex.Message}");
        }
    }



}

