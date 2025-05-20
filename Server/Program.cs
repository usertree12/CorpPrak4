using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
namespace CodeMasterServer
{
    public class GameResult
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public string SecretCode { get; set; }
        public List<PlayerResult> PlayerResults { get; set; }
        public string Winner { get; set; }
    }
    public class PlayerResult
    {
        public string PlayerName { get; set; }
        public int Attempts { get; set; }
    }
    class Program
    {
        private const int Port = 5000;
        private static readonly List<Player> players = new();
        private static string secretCode;
        private static int maxAttemptsPerPlayer = 10;
        private static int currentPlayerIndex = 0;
        private static DateTime roundStartTime;
        private static readonly object gameLock = new();
        static async Task Main(string[] args)
        {
            TcpListener server = new(IPAddress.Any, Port);
            server.Start();
            Console.WriteLine("Сервер запущен... Ожидание минимум 2 игроков для запуска игры.");
            while (true)
            {
                var client = await server.AcceptTcpClientAsync();
                lock (gameLock)
                {
                    if (players.Count >= 4) // Проверка на макс количество игроков
                    {
                        Console.WriteLine("Максимальное число игроков достигнуто.");
                        client.Close();
                        continue;
                    }
                    var player = new Player(client, $"Игрок {players.Count + 1}");
                    players.Add(player);
                    Console.WriteLine($"Подключился {player.Name}. Игроков в игре: {players.Count}");
                    _ = HandleClientAsync(player);
                }
                if (players.Count == 2)// Запускаем игру когда подключились минимум 2 игрока
                {
                    StartNewRound();
                }
            }
        }
        private static void StartNewRound()
        {
            lock (gameLock)
            {
                secretCode = GenerateSecretCode();
                Console.WriteLine($"Новый раунд запущен. Секретный код: {secretCode}");
                roundStartTime = DateTime.Now;
                foreach (var p in players)
                {
                    p.Attempts = 0;
                    p.SendMessageAsync($"Новый раунд начался! Тайна кода из 4 символов. Играют {players.Count} игрока(ов).").Wait();
                }
                currentPlayerIndex = 0;
                NotifyCurrentTurn().Wait();
            }
        }
        private static async Task NotifyCurrentTurn()
        {
            for (int i = 0; i < players.Count; i++)
            {
                if (i == currentPlayerIndex)
                {
                    await players[i].SendMessageAsync("Ваш ход! Введите вашу догадку (4 символа):");
                }
                else
                {
                    await players[i].SendMessageAsync($"Ожидайте ход игрока {players[currentPlayerIndex].Name}.");
                }
            }
        }
        private static async Task HandleClientAsync(Player player)
        {
            try
            {
                NetworkStream stream = player.Client.GetStream();
                byte[] buffer = new byte[512];
                while (player.Client.Connected)
                {
                    int byteCount = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (byteCount == 0)
                    {
                        // Клиент отключился
                        Console.WriteLine($"{player.Name} отключился.");
                        lock (gameLock)
                        {
                            players.Remove(player);
                        }
                        break;
                    }
                    string guess = Encoding.UTF8.GetString(buffer, 0, byteCount).Trim().ToUpper();
                    lock (gameLock)
                    {
                        if (players[currentPlayerIndex] != player)  // Проверка очереди
                        {
                            _ = player.SendMessageAsync("Сейчас не ваш ход, пожалуйста, ждите вашей очереди.");
                            continue;
                        }
                        if (guess.Length != 4)
                        {
                            _ = player.SendMessageAsync("Догадка должна содержать ровно 4 символа (A-Z, 0-9). Попробуйте снова:");
                            continue;
                        }
                        player.Attempts++;
                        if (guess == secretCode)
                        {
                            _ = player.SendMessageAsync("Поздравляем! Вы угадали секретный код!");  // Победа игрока
                            foreach (var p in players)
                            {
                                if (p != player)
                                {
                                    _ = p.SendMessageAsync($"{player.Name} угадал код и победил!");
                                }
                            }
                            SaveGameResult(player).Wait();
                            StartNewRound(); // Запустить новый раунд
                            return;
                        }
                        else
                        {
                            var (black, white) = GetMarkers(guess);
                            _ = player.SendMessageAsync($"Черные маркеры: {black}, Белые маркеры: {white}");
                            currentPlayerIndex = (currentPlayerIndex + 1) % players.Count;// Переключаем ход на следующего игрока
                            NotifyCurrentTurn().Wait();
                        }
                        if (player.Attempts >= maxAttemptsPerPlayer) // Проверка на то, не превысил ли игрок лимит попыток
                        {
                            _ = player.SendMessageAsync("Вы исчерпали все попытки этого раунда.");
                            currentPlayerIndex = (currentPlayerIndex + 1) % players.Count;
                            NotifyCurrentTurn().Wait();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка с игроком {player.Name}: {ex.Message}");
                lock (gameLock)
                {
                    players.Remove(player);
                }
            }
        }
        private static (int black, int white) GetMarkers(string guess)
        {
            int black = 0, white = 0;
            var secretCharCount = new Dictionary<char, int>();
            var guessCharCount = new Dictionary<char, int>();
            for (int i = 0; i < 4; i++)
            {
                if (guess[i] == secretCode[i])
                {
                    black++;
                }
                else
                {
                    if (secretCharCount.ContainsKey(secretCode[i]))
                        secretCharCount[secretCode[i]]++;
                    else
                        secretCharCount[secretCode[i]] = 1;
                    if (guessCharCount.ContainsKey(guess[i]))
                        guessCharCount[guess[i]]++;
                    else
                        guessCharCount[guess[i]] = 1;
                }
            }
            foreach (var kvp in guessCharCount)
            {
                if (secretCharCount.ContainsKey(kvp.Key))
                {
                    white += Math.Min(kvp.Value, secretCharCount[kvp.Key]);
                }
            }
            return (black, white);
        }
        private static string GenerateSecretCode()
        {
            Random rnd = new();
            char[] chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".ToCharArray();
            char[] code = new char[4];
            for (int i = 0; i < code.Length; i++)
            {
                code[i] = chars[rnd.Next(chars.Length)];
            }
            return new string(code);
        }
        private static async Task SaveGameResult(Player winner)
        {
            var gameResult = new GameResult
            {
                StartTime = roundStartTime,
                EndTime = DateTime.Now,
                SecretCode = secretCode,
                PlayerResults = new List<PlayerResult>(),
                Winner = winner.Name
            };
            foreach (var p in players)
            {
                gameResult.PlayerResults.Add(new PlayerResult
                {
                    PlayerName = p.Name,
                    Attempts = p.Attempts
                });
            }
            string fileName = "game_results.xml";
            List<GameResult> results = new();
            if (File.Exists(fileName))
            {
                try
                {
                    XmlSerializer serializer = new(typeof(List<GameResult>));
                    using FileStream fs = new(fileName, FileMode.Open);
                    results = (List<GameResult>)serializer.Deserialize(fs);
                }
                catch // Ошибка чтения
                {
                    results = new List<GameResult>();
                }
            }
            results.Add(gameResult);
            XmlSerializer xmlSerializer = new(typeof(List<GameResult>));
            using FileStream fsWrite = new(fileName, FileMode.Create);
            xmlSerializer.Serialize(fsWrite, results);
            Console.WriteLine($"Раунд завершен. Победитель: {winner.Name}. Результаты сохранены.");
        }
    }
    public class Player
    {
        public TcpClient Client { get; }
        public string Name { get; }
        public int Attempts { get; set; }
        private readonly object sendLock = new();
        public Player(TcpClient client, string name)
        {
            Client = client;
            Name = name;
            Attempts = 0;
        }
        public async Task SendMessageAsync(string message)
        {
            try
            {
                byte[] data = Encoding.UTF8.GetBytes(message + Environment.NewLine);
                lock (sendLock)
                {
                    if (Client.Connected)
                    {
                        NetworkStream stream = Client.GetStream();
                        stream.WriteAsync(data, 0, data.Length).Wait();
                    }
                }
            }
            catch
            {
                // Игнорируемая ошибка
            }
        }
    }
}

