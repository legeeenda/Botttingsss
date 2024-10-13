using System;
using System.IO;
using System.Text.Json;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;  // <-  Добавьте эту директиву
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;






class Program
{
    private static ITelegramBotClient? client;
    private static ReceiverOptions? receiverOptions;
    private static string token = "6838708209:AAEdjlRTjSxMr39KtBsX-jV9nGcvfXeegr4"; // Замените на ваш токен
    private static string jsonFilePath = "raffles.json"; // Путь к файлу для сохранения розыгрышей
    private static string historyJsonFilePath = "raffleHistory.json"; // Путь к файлу для истории
    private static List<Raffle> raffles = new List<Raffle>();
    private static List<Raffle> raffleHistory = new List<Raffle>();
    private static Timer raffleTimer;
    private static TimeSpan checkInterval = TimeSpan.FromMinutes(0.1); // Интервал проверки таймера



    public static void Main(string[] args)
    {
        client = new TelegramBotClient(token);
        receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = new[] { UpdateType.Message, UpdateType.CallbackQuery }
        };

        LoadRaffles();

        raffleTimer = new Timer(CheckRaffles, null, TimeSpan.Zero, checkInterval);

        using var cts = new CancellationTokenSource();
        client.StartReceiving(UpdateHandler, ErrorHandler, receiverOptions, cts.Token);

        Console.WriteLine("Бот работает в автономном режиме!");
        Console.ReadLine();
        Console.WriteLine("Бот остановлен полностью");

    }

    private static void CheckRaffles(object? state)
    {
        foreach (var raffle in raffles.ToList())
        {
            if (raffle.ScheduledTime <= DateTime.Now.TimeOfDay)
            {
                _ = SelectWinner(client, raffle);
                raffles.Remove(raffle);
                raffleHistory.Add(raffle);
                SaveRaffles();
                SaveRaffleHistory();
            }
        }
    }

    private static async Task UpdateHandler(ITelegramBotClient client, Update update, CancellationToken token)
    {
        if (update.Type == UpdateType.Message && update.Message?.Text != null)
        {
            var message = update.Message;
            var chatId = message.Chat.Id;
            List<long> adminIds = new List<long> { 786666022, 1097009099 };

            if (adminIds.Contains(chatId))
            {
                await HandleAdminCommands(client, message);
            }
            else if (message.Text == "/admin")
            {
                await client.SendTextMessageAsync(chatId, "** вы не администратор **");
            }

            if (message.Text == "/start")
            {
                await client.SendTextMessageAsync(message.Chat.Id, $"Привет {message.From?.Username}!");

                var startKeyboard = new InlineKeyboardMarkup(new[]
                {
                    InlineKeyboardButton.WithCallbackData("History", "show_history")
                });
                await client.SendTextMessageAsync(chatId, "История розыгрышей", replyMarkup: startKeyboard);
                await ShowRaffles(client, chatId);
            }
        }

        if (update.Type == UpdateType.CallbackQuery)
        {
            await HandleCallbackQuery(client, update.CallbackQuery);
        }
    }

    private static async Task HandleAdminCommands(ITelegramBotClient client, Message message)
    {
        var chatId = message.Chat.Id;

        if (message.Text == "/admin")
        {
            await client.SendTextMessageAsync(chatId, "Вы в панели администратора.\nСоздать розыгрыш: /create name\nУдалить розыгрыш: /delete name\nРедактировать название: /edit oldName newName\nУстановить время: /settime name HH:mm\nЗапустить розыгрыш: /starte name", replyMarkup: AdminPanel());
        }

        if (message.Text.StartsWith("/create"))
        {
            var parts = message.Text.Trim().Split(' ');

            if (parts.Length < 2)
            {
                await client.SendTextMessageAsync(chatId, "Пожалуйста, укажите название розыгрыша после команды /create");
                return;
            }

            string giveawayName = string.Join(' ', parts.Skip(1));
            var raffle = new Raffle(giveawayName);
            raffles.Add(raffle);
            SaveRaffles();

            await client.SendTextMessageAsync(chatId, $"Розыгрыш '{giveawayName}' создан!");
        }

        if (message.Text == "/history")
        {
            await ShowRaffleHistory(client, chatId);
        }

        if (message.Text.StartsWith("/delete"))
        {
            var parts = message.Text.Trim().Split(' ');

            if (parts.Length < 2)
            {
                await client.SendTextMessageAsync(chatId, "Пожалуйста, укажите название розыгрыша после команды /delete");
                return;
            }

            string giveawayName = string.Join(' ', parts.Skip(1));
            var raffle = raffles.FirstOrDefault(r => r.Name.Equals(giveawayName, StringComparison.OrdinalIgnoreCase));
            if (raffle != null)
            {
                raffles.Remove(raffle);
                SaveRaffles();
                await client.SendTextMessageAsync(chatId, $"Розыгрыш '{giveawayName}' удалён.");
            }
            else
            {
                await client.SendTextMessageAsync(chatId, $"Розыгрыш '{giveawayName}' не найден.");
            }
        }
        if (message.Text.StartsWith("/setimage"))
        {
            var parts = message.Text.Trim().Split(' ');

            if (parts.Length < 3)
            {
                await client.SendTextMessageAsync(chatId, "Использование: /setimage name URL");
                return;
            }

            string raffleName = parts[1];
            string imageUrl = parts[2];

            var raffle = raffles.FirstOrDefault(r => r.Name.Equals(raffleName, StringComparison.OrdinalIgnoreCase));
            if (raffle != null)
            {
                raffle.ImageURL = imageUrl;
                SaveRaffles();
                await client.SendTextMessageAsync(chatId, $"Картинка для розыгрыша \"{raffleName}\" установлена.");
            }
            else
            {
                await client.SendTextMessageAsync(chatId, $"Розыгрыш '{raffleName}' не найден.");
            }
        }

        if (message.Text.StartsWith("/edit"))
        {
            var parts = message.Text.Trim().Split(' ');

            if (parts.Length < 3)
            {
                await client.SendTextMessageAsync(chatId, "Использование: /edit OLDname NEWname");
                return;
            }

            string oldName = parts[1];
            string newName = string.Join(' ', parts.Skip(2));

            var raffle = raffles.FirstOrDefault(r => r.Name.Equals(oldName, StringComparison.OrdinalIgnoreCase));
            if (raffle != null)
            {
                raffle.Name = newName;
                SaveRaffles();
                await client.SendTextMessageAsync(chatId, $"Название розыгрыша изменено на '{newName}'.");
            }
            else
            {
                await client.SendTextMessageAsync(chatId, $"Розыгрыш '{oldName}' не найден.");
            }
        }

        if (message.Text.StartsWith("/settime"))
        {
            var parts = message.Text.Trim().Split(' ');

            if (parts.Length < 3)
            {
                await client.SendTextMessageAsync(chatId, "Использование: /settime name HH:mm");
                return;
            }

            string raffleName = parts[1];
            string timeStr = parts[2];

            if (!TimeSpan.TryParse(timeStr, out var scheduledTime))
            {
                await client.SendTextMessageAsync(chatId, "Пожалуйста, укажите время в формате HH:mm");
                return;
            }

            var raffle = raffles.FirstOrDefault(r => r.Name.Equals(raffleName, StringComparison.OrdinalIgnoreCase));
            if (raffle != null)
            {
                raffle.ScheduledTime = scheduledTime;
                SaveRaffles();
                await client.SendTextMessageAsync(chatId, $"Время розыгрыша \"{raffleName}\" установлено на {timeStr}");
            }
            else
            {
                await client.SendTextMessageAsync(chatId, $"Розыгрыш '{raffleName}' не найден.");
            }
        }

        if (message.Text.StartsWith("/starte"))
        {
            var parts = message.Text.Trim().Split(' ');

            if (parts.Length < 2)
            {
                await client.SendTextMessageAsync(chatId, "Пожалуйста, укажите название розыгрыша после команды /starte");
                return;
            }

            string raffleName = string.Join(' ', parts.Skip(1));
            var raffle = raffles.FirstOrDefault(r => r.Name.Equals(raffleName, StringComparison.OrdinalIgnoreCase));

            if (raffle != null)
            {
                if (raffle.ScheduledTime <= DateTime.Now.TimeOfDay)
                {
                    await SelectWinner(client, raffle);
                    raffles.Remove(raffle);
                    raffleHistory.Add(raffle);
                    SaveRaffles();
                    SaveRaffleHistory();
                }
                else
                {
                    await client.SendTextMessageAsync(chatId, $"Розыгрыш \"{raffle.Name}\" не может быть запущен до {raffle.ScheduledTime}");
                }
            }
            else
            {
                await client.SendTextMessageAsync(chatId, $"Розыгрыш '{raffleName}' не найден.");
            }
        }
    }

    private static async Task HandleCallbackQuery(ITelegramBotClient client, CallbackQuery callbackQuery)
    {
        if (callbackQuery?.Data != null)
        {
            var parts = callbackQuery.Data.Split('_');

            if (callbackQuery.Data == "show_history")
            {
                // Удаляем текущее сообщение с кнопкой
                await client.DeleteMessageAsync(callbackQuery.Message.Chat.Id, callbackQuery.Message.MessageId);

                // Показываем историю розыгрышей без кнопки
                await ShowRaffleHistory(client, callbackQuery.Message.Chat.Id);
            }
            else if (callbackQuery.Data == "close")
            {
                await client.DeleteMessageAsync(callbackQuery.Message.Chat.Id, callbackQuery.Message.MessageId);
            }
            else if (callbackQuery.Data.StartsWith("history_"))
            {
                string raffleId = parts[1];
                var raffle = raffleHistory.FirstOrDefault(r => r.Name == raffleId);
                if (raffle != null)
                {
                    // Объявление переменных перед использованием
                    string messageText = $":{raffle.ImageURL}\n" +
                              $"Розыгрыш: {raffle.Name}\n" +
                              $"Количество участников: {raffle.Participants.Count}\n" +
                              $"Победитель: {raffle.Participants.FirstOrDefault() ?? "Неизвестен"}\n" +
                              $"Дата проведения: {raffle.RaffleTime?.ToString("dd.MM.yyyy HH:mm") ?? "Ещё не проведён"}";

                    var markup = new InlineKeyboardMarkup(new[]
                    {
            InlineKeyboardButton.WithCallbackData("Назад", "show_history"),
            InlineKeyboardButton.WithCallbackData("Закрыть", "close")
          });

                    // Проверяем, есть ли картинка
                    if (!string.IsNullOrEmpty(raffle.ImageURL))
                    {
                        try
                        {
                            InputFileUrl photo = new InputFileUrl(raffle.ImageURL);
                            // Удаляем двоеточие и ссылку из messageText
                            string messageTextWithoutLink = messageText.Replace($":{raffle.ImageURL}\n", "");
                            await client.SendPhotoAsync(
                              callbackQuery.Message.Chat.Id,
                              photo,
                              caption: messageTextWithoutLink,
                              replyMarkup: markup);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Ошибка при отправке изображения: {ex.Message}");
                            await client.EditMessageTextAsync(
                              callbackQuery.Message.Chat.Id,
                              callbackQuery.Message.MessageId,
                              messageText,
                              replyMarkup: markup);
                        }
                    }
                    else
                    {
                        // Отправка только текстового сообщения, если нет картинки
                        await client.EditMessageTextAsync(
                          callbackQuery.Message.Chat.Id,
                          callbackQuery.Message.MessageId,
                          messageText,
                          replyMarkup: markup);
                    }
                }
            }

            else if (parts.Length == 2)
            {
                string action = parts[0];
                string raffleName = parts[1];
                var raffle = raffles.FirstOrDefault(r => r.Name.Equals(raffleName, StringComparison.OrdinalIgnoreCase));

                if (raffle != null)
                {
                    long participantId = callbackQuery.From.Id;

                    if (action == "participate")
                    {
                        if (!raffle.ParticipantIds.Contains(participantId))
                        {
                            raffle.ParticipantIds.Add(participantId);
                            raffle.Participants.Add(callbackQuery.From.Username ?? "Anonymous");
                            SaveRaffles();
                            await client.AnswerCallbackQueryAsync(callbackQuery.Id, "Вы успешно участвуете в розыгрыше!");

                            if (raffle.ScheduledTime <= DateTime.Now.TimeOfDay)
                            {

                            }
                            else
                            {

                            }
                        }
                        else
                        {
                            await client.AnswerCallbackQueryAsync(callbackQuery.Id, "Вы уже участвуете в этом розыгрыше.");
                        }
                    }
                    else if (action == "withdraw")
                    {
                        if (raffle.ParticipantIds.Contains(participantId))
                        {
                            raffle.ParticipantIds.Remove(participantId);
                            raffle.Participants.Remove(callbackQuery.From.Username ?? "Anonymous");
                            SaveRaffles();
                            await client.AnswerCallbackQueryAsync(callbackQuery.Id, "Вы покинули розыгрыш.");
                        }
                        else
                        {
                            await client.AnswerCallbackQueryAsync(callbackQuery.Id, "Вы не участвуете в этом розыгрыше.");
                        }

                    }
                }
                else
                {
                    await client.AnswerCallbackQueryAsync(callbackQuery.Id, "Розыгрыш уже завершён или не найден.");
                    // Удаляем сообщение с информацией о розыгрыше
                    await client.DeleteMessageAsync(callbackQuery.Message.Chat.Id, callbackQuery.Message.MessageId);
                }
            }
        }
    }





    private static async Task ShowRaffleHistory(ITelegramBotClient client, long chatId)
    {
        if (!raffleHistory.Any())
        {
            await client.SendTextMessageAsync(chatId, "История розыгрышей пуста.");
        }
        else
        {
            var buttons = raffleHistory.Select(r =>
              InlineKeyboardButton.WithCallbackData(r.Name, $"history_{r.Name}")).ToList();
                    var keyboard = new InlineKeyboardMarkup(buttons.Concat(new[] { InlineKeyboardButton.WithCallbackData("Закрыть", "close") }));
            await client.SendTextMessageAsync(chatId, "Выберите розыгрыш из истории:", replyMarkup: keyboard);
        }
    }

    private static async Task ShowRaffles(ITelegramBotClient client, long chatId)
    {
        if (!raffles.Any())
        {
            await client.SendTextMessageAsync(chatId, "На данный момент сейчас не доступны розыгрыши.");
        }
        else
        {
            foreach (var raffle in raffles)
            {
                var keyboard = RaffleActionButtons(raffle.Name);

                // Составляем сообщение с текстом и картинкой
                string messageText = $"Розыгрыш: {raffle.Name}\nКоличество участников: {raffle.Participants.Count}\nЗапланированное время: {raffle.ScheduledTime}";

                // Отправка сообщения с картинкой, если она есть
                if (!string.IsNullOrEmpty(raffle.ImageURL))
                {
                    try
                    {
                        // Используем InputOnlineFile для загрузки картинки по URL
                        InputFileUrl photo = new InputFileUrl(raffle.ImageURL);
                        await client.SendPhotoAsync(chatId, photo, caption: messageText, replyMarkup: keyboard);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Ошибка при отправке изображения: {ex.Message}");
                        // В случае ошибки отправляем только текст
                        await client.SendTextMessageAsync(chatId, messageText, replyMarkup: keyboard);
                    }
                }
                else
                {
                    // Отправка только текстового сообщения, если нет картинки
                    await client.SendTextMessageAsync(chatId, messageText, replyMarkup: keyboard);
                }
            }
        }
    }






    private static InlineKeyboardMarkup RaffleActionButtons(string raffleName)
    {
        return new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("Участвовать", $"participate_{raffleName}"),
                InlineKeyboardButton.WithCallbackData("Отписаться", $"withdraw_{raffleName}")
            }
        });
    }

    private static void LoadRaffles()
    {
        if (System.IO.File.Exists(jsonFilePath))
        {
            var json = System.IO.File.ReadAllText(jsonFilePath);
            raffles = JsonSerializer.Deserialize<List<Raffle>>(json) ?? new List<Raffle>();
        }

        if (System.IO.File.Exists(historyJsonFilePath))
        {
            var json = System.IO.File.ReadAllText(historyJsonFilePath);
            raffleHistory = JsonSerializer.Deserialize<List<Raffle>>(json) ?? new List<Raffle>();
        }
    }

    private static void SaveRaffles()
    {
        var json = JsonSerializer.Serialize(raffles);
        System.IO.File.WriteAllText(jsonFilePath, json);
    }

    private static void SaveRaffleHistory()
    {
        var json = JsonSerializer.Serialize(raffleHistory);
        System.IO.File.WriteAllText(historyJsonFilePath, json);
    }

    private static InlineKeyboardMarkup AdminPanel()
    {
        return new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("Запустить розыгрыш", "/starte name") }
        });
    }

    private static Task ErrorHandler(ITelegramBotClient client, Exception exception, CancellationToken token)
    {
        Console.WriteLine($"Ошибка: {exception.Message}");
        return Task.CompletedTask;
    }

    private static async Task SelectWinner(ITelegramBotClient client, Raffle raffle)
    {
        if (raffle.Participants.Any())
        {
            raffle.RaffleTime = DateTime.Now;  // Устанавливаем время проведения розыгрыша

            Random rand = new Random();
            int winnerIndex = rand.Next(raffle.Participants.Count);
            string winnerName = raffle.Participants[winnerIndex];
            long winnerId = raffle.ParticipantIds[winnerIndex];

            string messageText = $"Поздравляем! Вы победитель розыгрыша '{raffle.Name}'!";
            await client.SendTextMessageAsync((int)winnerId, messageText);

            foreach (var participantId in raffle.ParticipantIds)
            {
                if (participantId != winnerId)
                {
                    await client.SendTextMessageAsync((int)participantId, $"Вы не выиграли в розыгрыше '{raffle.Name}'. Спасибо за участие!");
                }
            }

            string participantList = string.Join(", ", raffle.Participants);
            foreach (var participantId in raffle.ParticipantIds)
            {
                await client.SendTextMessageAsync((int)participantId, $"Результаты розыгрыша '{raffle.Name}'\nПобедитель - {winnerName}.\n\n\nПолный список участников: {participantList}");
            }
        }
        else
        {
            string noParticipantsMessage = $"В розыгрыше '{raffle.Name}' нет участников.";
            foreach (var participantId in raffle.ParticipantIds)
            {
                await client.SendTextMessageAsync((int)participantId, noParticipantsMessage);
            }
        }
    }

    public class Raffle
    {

        public string Name { get; set; }
        public List<string> Participants { get; set; }
        public List<long> ParticipantIds { get; set; }
        public TimeSpan? ScheduledTime { get; set; }

        public DateTime? RaffleTime { get; set; } // Новое свойство

        public string ImageURL { get; set; } // Новое свойство для URL картинки
        public Raffle(string name)
        {
            Name = name;
            Participants = new List<string>();
            ParticipantIds = new List<long>();
        }
    }
}