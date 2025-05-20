using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
namespace CodeMasterClient
{
    class Program
    {
        private const string ServerIp = "127.0.0.1";
        private const int ServerPort = 5000;
        static async Task Main(string[] args)
        {
            using (TcpClient client = new TcpClient(ServerIp, ServerPort))
            {
                NetworkStream stream = client.GetStream();
                while (true)
                {
                    // Ожидание сообщения от сервера
                    byte[] buffer = new byte[512];
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    string response = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
                    Console.WriteLine(response);
                    if (response.StartsWith("Ваш ход!"))
                    {
                        Console.WriteLine("Введите вашу догадку (4 символа):");
                        string guess = Console.ReadLine();
                        if (guess.Length != 4)
                        {
                            Console.WriteLine("Догадка должна содержать ровно 4 символа (A-Z, 0-9). Попробуйте снова:");
                            continue;
                        }
                        byte[] data = Encoding.UTF8.GetBytes(guess);
                        await stream.WriteAsync(data, 0, data.Length);
                    }
                }
            }
        }
    }
}