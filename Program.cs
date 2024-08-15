using HtmlAgilityPack;
using System.Net;
using Spectre.Console;
using System.Data;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System;
using System.Reflection;

namespace ForentKemonoUltraDownloader
{
    internal class Program
    {

        private const int MF_BYCOMMAND = 0x00000000;
        public const int SC_MAXIMIZE = 0xF030;
        public const int SC_SIZE = 0xF000;

        private static string baseSiteUrl = "https://kemono.su";
        private static string searchFilter = String.Empty;
        private static string siteConnection = String.Empty;

        private static SemaphoreSlim requestSemaphore = new SemaphoreSlim(12);

        private static HttpClient httpClient;
        private static HttpClientHandler httpClientHandler;
        private static int timeout = 1;

        private static readonly Random random = new Random();

        private static Color colorMain = Color.DarkOrange;
        private static Color colorSubMain = Color.Gold1;

        private static string markupMain = "[darkorange]";
        private static string markupSubMain = "[gold1]";
        private static string markupSub2 = "[darkolivegreen1]";

        private static int windowWidth;
        private static int windowHeight;
        private static int heightOfSemiscreenPanel;

        private static ConcurrentQueue<string> errorQueue = new ConcurrentQueue<string>();
        private static Action UpdateErrorPanelAction;

        private static string? postFilter = "";
        private static double elapsedTime = 0;
        private static int totalDownloadedFiles = 0;
        private static double totalDownloadedSizeInMB = 0;
        private static int totalAuthorsWithPosts = 0;
        private static readonly object lockObject = new object();

        [DllImport("user32.dll")]
        public static extern int DeleteMenu(IntPtr hMenu, int nPosition, int wFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetSystemMenu(IntPtr hWnd, bool bRevert);

        [DllImport("kernel32.dll", ExactSpelling = true)]
        private static extern IntPtr GetConsoleWindow();
        public class AppSettings
        {
            public string SaveDirectoryPath { get; set; } = String.Empty;
            public string AuthorFilePath { get; set; } = String.Empty;
            public List<string> DownloadOptions { get; set; } = new List<string>();
            public string ProxyAdress { get; set; } = String.Empty;
        }

        static AppSettings appSettings = new AppSettings();

        static Program()
        {
            httpClientHandler = new HttpClientHandler();
            httpClient = new HttpClient(httpClientHandler);
        }

        static void InitializeHttpClient()
        {
            var handler = new HttpClientHandler();

            if (appSettings.ProxyAdress != "Отсутствует")
            {
                handler.Proxy = new WebProxy(appSettings.ProxyAdress);
                handler.UseProxy = true;
            }
            else
            {
                handler.Proxy = null;
                handler.UseProxy = false;
            }

            httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(timeout)
            };
        }

        static async Task Main(string[] args)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "ForentKemonoUltraDownloader.Resources.ANSI Shadow.flf";

            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                if (!File.Exists("Figlet.flf"))
                    using (FileStream fileStream = new FileStream("Figlet.flf", FileMode.CreateNew, FileAccess.Write))
                        stream.CopyTo(fileStream);


            DeleteMenu(GetSystemMenu(GetConsoleWindow(), false), SC_MAXIMIZE, MF_BYCOMMAND);
            DeleteMenu(GetSystemMenu(GetConsoleWindow(), false), SC_SIZE, MF_BYCOMMAND);

            if (!File.Exists("appsettings.json"))
                InitializeSettings();
            
            LoadSettings();
            InitializeHttpClient();
            siteConnection = await CheckSiteConnection(baseSiteUrl) == true ? "Установлено" : "Отсутствует";

            ConsoleHelper.SetCurrentFont("Consolas", 18);
            windowWidth = (int)(Console.LargestWindowWidth * 0.85);
            windowHeight = (int)(Console.LargestWindowHeight * 0.75);
            heightOfSemiscreenPanel = (windowHeight / 2) - 2;
            Console.SetWindowSize(windowWidth, windowHeight);
            Console.SetBufferSize(windowWidth, windowHeight);


            WriteMainInfo();
            await WriteMainMenu();

        }

        static void WriteLogo()
        {
            var font = FigletFont.Load("Figlet.flf");
            var figlet = new FigletText(font, "FKUD").Color(colorMain);
            var titleFull = new Markup("[bold white]\n\nForent's\nKemono\nUltra\nDownloader[/]");

            var gridLogo = new Grid()
            .AddColumn(new GridColumn().Width(33).PadLeft(1))
            .AddColumn(new GridColumn().PadLeft(1));
            gridLogo.AddRow(figlet, titleFull);
            AnsiConsole.Write(gridLogo);
        }

        static void WriteMainInfo()
        {
            var connectionPanel = new Panel(new Markup($"Прокси: {markupSubMain}{appSettings.ProxyAdress}[/]\n" +
                $"Интернет соеденение с сайтом {markupMain}{baseSiteUrl}[/]: {markupSubMain}{siteConnection}[/]"))
                .Header(new PanelHeader($"{markupMain} Соединение [/]"))
                .BorderColor(colorMain)
                .Padding(1, 0, 1, 0);

            var gridParameters = new Grid()
                .AddColumn(new GridColumn())
                .AddColumn(new GridColumn())
                .AddRow(new Text("Путь файла списка авторов:"), new TextPath(appSettings.AuthorFilePath).LeafColor(colorSubMain).SeparatorColor(colorSubMain).RootColor(colorSubMain).StemColor(colorSubMain))
                .AddRow(new Text("Путь сохранения файлов:"), new TextPath(appSettings.SaveDirectoryPath).LeafColor(colorSubMain).SeparatorColor(colorSubMain).RootColor(colorSubMain).StemColor(colorSubMain))
                .AddRow(new Text("Варианты загрузки: "), new Text(string.Join(", ", appSettings.DownloadOptions), colorSubMain));

            var parametersPanel = new Panel(gridParameters)
                .Header(new PanelHeader($"{markupMain} Параметры [/]"))
                .Border(BoxBorder.Double)
                .BorderColor(colorMain)
                .Padding(1, 0, 1, 0);


            AnsiConsole.WriteLine();
            WriteLogo();
            AnsiConsole.Write(connectionPanel);
            AnsiConsole.Write(parametersPanel);
            AnsiConsole.Write(new Markup($" [grey](Нажмите {markupMain}<Ctrl + C>[/] для выхода из программы)[/]\n\n"));
        }

        static void WriteStatistics()
        {
            AnsiConsole.Clear();

            var gridParameters = new Grid()
                .AddColumn(new GridColumn())
                .AddColumn(new GridColumn())
                .AddRow(new Text("Заданный фильтр: "), new Text($"{postFilter}", colorSubMain))
                .AddRow(new Text("Время выполнения: "), new Text($"{elapsedTime:F1} с", colorSubMain))
                .AddRow(new Text("Загружено файлов: "), new Text($"{totalDownloadedFiles}", colorSubMain))
                .AddRow(new Text("Общий размер файлов: "), new Text($"{totalDownloadedSizeInMB} МБ", colorSubMain))
                .AddRow(new Text("Авторов с постами: "), new Text($"{totalAuthorsWithPosts}", colorSubMain));

            var parametersPanel = new Panel(gridParameters)
                .Header(new PanelHeader($"{markupMain} Статистика [/]"))
                .Border(BoxBorder.Double)
                .BorderColor(colorMain)
                .Padding(1, 0, 1, 0);

            AnsiConsole.WriteLine();
            WriteLogo();
            AnsiConsole.Write(parametersPanel);
        }

        static async Task UpdateMainScreen(int menuIndex = 0)
        {
            AnsiConsole.Clear();
            WriteMainInfo();
            if (menuIndex == 0)
                await WriteMainMenu();
            else
                await WriteParameters();
        }

        static async Task WriteMainMenu()
        {
            var options = new[] { "Начать", "Параметры" };
            var mainMenuPrompt = AnsiConsole.Prompt(new SelectionPrompt<string>()
                    .WrapAround(true)
                    .Title($" Выберите {markupMain}опцию[/]:")
                    .HighlightStyle(colorMain)
                    .AddChoices(options)
                    );

            int selectedOption = Array.IndexOf(options, mainMenuPrompt);

            switch (selectedOption)
            {
                case 0:
                    if (siteConnection == "Установлено")
                        await Process();
                    else
                        await ShowConnectionError();
                    break;
                case 1: await WriteParameters(); break;
            }
        }

        static async Task ShowConnectionError()
        {
            AnsiConsole.MarkupLine("[red] Ошибка: Соединение с сайтом отсутствует[/]");
            AnsiConsole.MarkupLine(" Пожалуйста, проверьте параметры и попробуйте снова");

            var backPrompt = AnsiConsole.Prompt(new SelectionPrompt<string>()
                    .WrapAround(true)
                    .HighlightStyle(colorMain)
                    .AddChoices("Назад").Title(String.Empty)
                    );

            if (backPrompt == "Назад")
                await UpdateMainScreen();
        }

        static async Task Process()
        {
            await StartProcess();
            WriteStatistics();
            await WriteStatisticsMenu();
        }

        
        static async Task WriteParameters()
        {
            var options = new[] { "Путь файла списка авторов", "Путь сохранения файлов", "Варианты скачивания", $"Установить прокси", $"{markupSubMain}Назад[/]" };
            var parametersPrompt = AnsiConsole.Prompt(new SelectionPrompt<string>()
                    .WrapAround(true)
                    .Title($" Выберите опцию для {markupMain}изменения[/]:")
                    .HighlightStyle(colorMain)
                    .AddChoices(options)
                    );

            int selectedOption = Array.IndexOf(options, parametersPrompt);

            switch (selectedOption)
            {
                case 0: await ChangeAuthorPath(); break;
                case 1: await ChangeSavePath(); break;
                case 2: await ChangeDownloadOptions(); break;
                case 3: await ChangeProxy(); break;
                case 4: await UpdateMainScreen(); break;
            }

        }

        static async Task WriteStatisticsMenu()
        {
            var options = new[] { "Продолжить", "Главное меню" };
            var parametersPrompt = AnsiConsole.Prompt(new SelectionPrompt<string>()
                    .WrapAround(true)
                    .Title($" Выберите {markupMain}опцию[/]:")
                    .HighlightStyle(colorMain)
                    .AddChoices(options)
                    );

            int selectedOption = Array.IndexOf(options, parametersPrompt);

            switch (selectedOption)
            {
                case 0: await Process(); break;
                case 1: await UpdateMainScreen(); break;   
            }
        }

        static string NormalizePath(string path) => path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        static async Task ChangeAuthorPath()
        {

            var saveFilePath = AnsiConsole.Prompt(
                new TextPrompt<string>($" Пожалуйста, укажите путь к списку авторов в формате {markupMain}.txt[/]:")
                    .PromptStyle(colorSubMain)
                    .Validate(filePath =>
                    {
                        var normalizedFilePath = NormalizePath(filePath);
                        var directory = Path.GetDirectoryName(filePath);
                        if (!Directory.Exists(directory))
                            return ValidationResult.Error("[red]Директория не существует[/]");

                        if (!File.Exists(filePath))
                            return ValidationResult.Error("[red]Указанный файл не существует[/]");

                        return ValidationResult.Success();
                    }));
            
            appSettings.AuthorFilePath = saveFilePath;
            SaveSettings();
            await UpdateMainScreen(1);
        }

        static async Task ChangeSavePath()
        {
            var saveDirPath = AnsiConsole.Prompt(
                new TextPrompt<string>(" Пожалуйста, укажите директорию для сохранения файлов:")
                    .PromptStyle(colorSubMain)
                    .Validate(filePath =>
                    {
                        var normalizedFilePath = NormalizePath(filePath);
                        if (!normalizedFilePath.EndsWith('/'))
                            normalizedFilePath += "/";

                        var directory = Path.GetDirectoryName(normalizedFilePath);
                        if (!Directory.Exists(directory))
                            return ValidationResult.Error("[red]Директория не существует[/]");

                        return ValidationResult.Success();
                    }));

            appSettings.SaveDirectoryPath = saveDirPath;
            SaveSettings();
            await UpdateMainScreen(1);
        }

        static async Task ChangeDownloadOptions()
        {
            var options = new[] { "Изображения", "Архивы", "Видео" };
            var types = AnsiConsole.Prompt(
                new MultiSelectionPrompt<string>()
                    .Title(" Пожалуйста, выберите типы загрузки:")
                    .PageSize(10)
                    .HighlightStyle(colorMain)
                    .InstructionsText($" [grey](Нажмите {markupMain}<Пробел>[/] для выбора, {markupMain}<Enter>[/] для принятия)[/]")
                    .WrapAround(true)
                    .AddChoiceGroup("Все типы", options));

            appSettings.DownloadOptions = types;
            SaveSettings();
            await UpdateMainScreen(1);

        }

        static async Task ChangeProxy()
        {
            var proxyAdress = AnsiConsole.Prompt(
                new TextPrompt<string>($" Пожалуйста, укажите прокси или оставьте пустым (например {markupMain}http://x.xx.xx.xxx:xxxxx[/]):")
                    .PromptStyle(colorSubMain)
                    .AllowEmpty());

            if (string.IsNullOrEmpty(proxyAdress))
                proxyAdress = "Отсутствует";

            appSettings.ProxyAdress = proxyAdress;
            SaveSettings();

            timeout = 1;
            InitializeHttpClient();
            siteConnection = await CheckSiteConnection(baseSiteUrl) == true ? "Установлено" : "Отсутствует";
            
            await UpdateMainScreen(1);
        }


        static async Task StartProcess()
        {
            var filter = AnsiConsole.Prompt(
                new TextPrompt<string>($" Введите фильтр для поиска (или оставьте пустым [grey](не рекомендуется)[/]):")
                    .PromptStyle(colorSubMain).AllowEmpty());

            postFilter = filter;
            AnsiConsole.Clear();

            var recentAuthors = new ConcurrentQueue<(string authorId, string authorName, int totalPosts)>();
            var recentDownloads = new ConcurrentQueue<(string FileName, string FileSize)>();
            var authorPostCounts = new ConcurrentDictionary<string, (string authorName, int totalPosts, int downloadedPosts)>();

            var layout = new Layout()
                .SplitRows(
                    new Layout("Top")
                        .SplitColumns(
                            new Layout("TopLeft").Ratio(1),
                            new Layout("TopRight").Ratio(3)),
                    new Layout("Bottom")
                        .SplitColumns(
                            new Layout("BottomLeft").Ratio(1),
                            new Layout("BottomRight").Ratio(1)));

            var topLeftPanel = new Panel("")
                .Header(new PanelHeader($"{markupMain} Сканирование авторов [/]").Centered())
                .Border(BoxBorder.Rounded)
                .Expand()
                .Padding(0, 0)
                .BorderColor(colorMain);
            layout["TopLeft"].Update(topLeftPanel);

            var topRightPanel = new Panel("")
                .Header(new PanelHeader($"{markupMain} Об авторах [/]").Centered())
                .Border(BoxBorder.Rounded)
                .Expand()
                .Padding(0, 0)
                .BorderColor(colorMain);
            layout["TopRight"].Update(topRightPanel);

            var bottomLeftPanel = new Panel("")
                .Header(new PanelHeader($"{markupMain} Загрузка [/]").Centered())
                .Border(BoxBorder.Rounded)
                .Expand()
                .BorderColor(colorMain);
            layout["BottomLeft"].Update(bottomLeftPanel);

            var bottomRightPanel = new Panel("")
                .Header(new PanelHeader($"[red1] Ошибки [/]").Centered())
                .Border(BoxBorder.Rounded)
                .Expand()
                .BorderColor(Color.Red1);
            layout["BottomRight"].Update(bottomRightPanel);

            timeout = 120;
            InitializeHttpClient();

            await AnsiConsole.Live(layout).StartAsync(async ctx =>
            {
                var stopwatch = Stopwatch.StartNew();
                UpdateErrorPanelAction = () => UpdateErrorPanel(ctx, layout);

                searchFilter = filter;
                string baseUrl = baseSiteUrl;

                Directory.CreateDirectory(appSettings.SaveDirectoryPath);

                var authors = (await File.ReadAllLinesAsync(appSettings.AuthorFilePath))
                    .Select(line => line.Trim())
                    .Where(line => !string.IsNullOrEmpty(line))
                    .ToList();

                SemaphoreSlim authorSemaphore = new SemaphoreSlim(3);
                SemaphoreSlim postSemaphore = new SemaphoreSlim(4);
                SemaphoreSlim contentSemaphore = new SemaphoreSlim(10);

                var tasks = new List<Task>();


                foreach (var authorId in authors)
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        await authorSemaphore.WaitAsync();
                        try { await ProcessAuthor(authorId, baseUrl, appSettings.SaveDirectoryPath, postSemaphore, contentSemaphore, recentAuthors, recentDownloads, authorPostCounts, ctx, layout); }
                        finally { authorSemaphore.Release(); }

                    }));
                }

                await Task.WhenAll(tasks);
                Console.Beep();
                stopwatch.Stop();
                elapsedTime = stopwatch.Elapsed.TotalSeconds;
            });
        }

        static async Task ProcessAuthor(
            string authorEntry,
            string baseUrl,
            string saveDirectory,
            SemaphoreSlim postSemaphore,
            SemaphoreSlim contentSemaphore,
            ConcurrentQueue<(string, string, int)> recentAuthors,
            ConcurrentQueue<(string FileName, string FileSize)> recentDownloads,
            ConcurrentDictionary<string, (string authorName, int totalPosts, int downloadedPosts)> authorPostCounts,
            LiveDisplayContext ctx,
            Layout layout)
        {

            string platform = authorEntry.Substring(0, 1);
            string authorId = authorEntry.Substring(2);
            string platformUrl = platform == "P" ? "/patreon/user/" : platform == "F" ? "/fanbox/user/" : null;

            if (platformUrl == null) return;

            var userUrl = $"{baseUrl}{platformUrl}{authorId}";

            int pageOffset = 0;
            bool hasPages = true;
            var postIdList = new List<string>();

            string authorName = "";

            while (hasPages)
            {
                var url = $"{userUrl}?o={pageOffset}&q={Uri.EscapeDataString(searchFilter)}";
                var html = await GetHtmlAsync(url);

                if (html == null)
                {
                    errorQueue.Enqueue($"[red]Ошибка получения HTML для {url}[/]");
                    UpdateErrorPanelAction?.Invoke();
                    break;
                }

                var htmlDoc = new HtmlDocument();
                htmlDoc.LoadHtml(html);

                if (htmlDoc.DocumentNode == null)
                {
                    errorQueue.Enqueue($"[red]Ошибка загрузки HTML[/]");
                    UpdateErrorPanelAction?.Invoke();
                    break;
                }

                var nameNode = htmlDoc.DocumentNode.SelectSingleNode("//span[@itemprop='name']");
                authorName = authorName = nameNode?.InnerText.Trim() ?? authorId;

                var nodesPostCard = htmlDoc.DocumentNode.SelectNodes("//article[contains(@class, 'post-card post-card--preview')]");

                if (nodesPostCard != null && nodesPostCard.Count > 0)
                {
                    foreach (var node in nodesPostCard)
                    {
                        var dataId = node.GetAttributeValue("data-id", "not found");
                        if (dataId != "not found")
                            postIdList.Add(dataId);
                    }
                    pageOffset += 50;
                }
                else
                    hasPages = false;

                await Task.Delay(100 + random.Next(200));
            }

            var totalPostCount = postIdList.Count;
            authorPostCounts[authorId] = (authorName, totalPostCount, 0);

            if (recentAuthors.Count >= heightOfSemiscreenPanel - 3)
                recentAuthors.TryDequeue(out _);
            recentAuthors.Enqueue((authorId, authorName, totalPostCount));

            var tableLeft = new Table()
                .AddColumn(new TableColumn("ID Автора"))
                .AddColumn(new TableColumn("Кол-во постов").Centered())
                .Border(TableBorder.Simple)
                .Expand()
                .BorderColor(colorMain);

            foreach (var author in recentAuthors.Take(heightOfSemiscreenPanel - 3))
            {
                tableLeft.AddRow($"{markupSubMain}{author.Item1}[/]", $"{markupSub2}{author.Item3}[/]");
            }

            var updatedTopLeftPanel = new Panel(tableLeft)
                .Header($"{markupMain} Сканирование авторов [/]", Justify.Center)
                .BorderColor(colorMain)
                .Expand()
                .Padding(0, 0);

            layout["TopLeft"].Update(updatedTopLeftPanel);
            ctx.Refresh();

            if (totalPostCount > 0)
            {

                string authorSaveDirectory = Path.Combine(saveDirectory, authorName);
                Directory.CreateDirectory(authorSaveDirectory);

                var postTasks = new List<Task>();

                foreach (var postId in postIdList)
                {
                    postTasks.Add(Task.Run(async () =>
                    {
                        await postSemaphore.WaitAsync();
                        try 
                        { 
                            await ProcessPost(postId, userUrl, authorSaveDirectory, contentSemaphore, recentDownloads, ctx, layout);
                            var currentCount = authorPostCounts[authorId];
                            authorPostCounts[authorId] = (currentCount.authorName , currentCount.totalPosts, currentCount.downloadedPosts + 1);
                        }
                        finally { postSemaphore.Release(); }
                    }));
                }

                await Task.WhenAll(postTasks);

                lock (lockObject)
                {
                    totalAuthorsWithPosts++;
                }
            }

            int tableRightWidth = ((windowWidth / 4) * 3) - 2;

            var tableRight = new Table()
                .AddColumn(new TableColumn("ID Автора").Width(tableRightWidth / 4))
                .AddColumn(new TableColumn("Имя автора").Width(tableRightWidth / 2))
                .AddColumn(new TableColumn("Загружено постов").Width(tableRightWidth / 4).Centered())
                .Border(TableBorder.Simple)
                .Expand()
                .BorderColor(colorMain);

            foreach (var kvp in authorPostCounts.Where(kvp => kvp.Value.totalPosts > 0))
            {
                tableRight.AddRow(
                    $"{markupSubMain}{kvp.Key}[/]", 
                    $"{markupSub2}{kvp.Value.authorName}[/]", 
                    $"{markupSubMain}{kvp.Value.downloadedPosts} из {kvp.Value.totalPosts}[/]");
            }

            var updatedTopRightPanel = new Panel(tableRight)
                .Header($"{markupMain} Об авторах [/]", Justify.Center)
                .BorderColor(colorMain)
                .Expand()
                .Padding(0, 0);

            layout["TopRight"].Update(updatedTopRightPanel);
            ctx.Refresh();
        }
       
        static async Task ProcessPost(string postId, string userUrl, string authorSaveDirectory, SemaphoreSlim contentSemaphore, ConcurrentQueue<(string FileName, string FileSize)> recentDownloads, LiveDisplayContext ctx, Layout layout)
        {
            string postUrl = $"{userUrl}/post/{postId}";
            var postHtml = await GetHtmlAsync(postUrl);


            if (postHtml == null)
            {
                errorQueue.Enqueue($"[red]Ошибка получения HTML поста: {postId}[/]");
                UpdateErrorPanelAction?.Invoke();
                return;
            }

            var postHtmlDoc = new HtmlDocument();
            postHtmlDoc.LoadHtml(postHtml);


            if (postHtmlDoc.DocumentNode == null)
            {
                errorQueue.Enqueue($"[red]Ошибка загрузки HTML поста: {postId}[/]");
                UpdateErrorPanelAction?.Invoke();
                return;
            }

            var postTitleNode = postHtmlDoc.DocumentNode.SelectSingleNode("//h1[@class='post__title']/span[1]");
            var postTitle = postTitleNode?.InnerText ?? postId;

            var attachmentsNode = postHtmlDoc.DocumentNode.SelectNodes("//a[contains(@class, 'fileThumb') or contains(@class, 'post__attachment-link')]");
            if (attachmentsNode == null || !attachmentsNode.Any())
            {
                errorQueue.Enqueue($"[red]Пост {postId} не содержит вложений. Пропускаю.[/]");
                UpdateErrorPanelAction?.Invoke();
                return;
            }

            var postSaveDirectory = Path.Combine(authorSaveDirectory, Sanitize(postTitle));
            Directory.CreateDirectory(postSaveDirectory);

            var fileTasks = new List<Task>();
            var fileTypes = new Dictionary<string, string>
            {
                {"Изображения", "//a[contains(@class, 'fileThumb')]" },
                {"Архивы", "//a[contains(@class, 'post__attachment-link') and (contains(@href, '.zip') or contains(@href, '.rar') or contains(@href, '.7z'))]" },
                { "Видео", "//a[contains(@class, 'post__attachment-link') and (contains(@href, '.mp4') or contains(@href, '.mov') or contains(@href, '.wmv') or contains(@href, '.flv') or contains(@href, '.avi'))]" }
            };

            foreach(var option in appSettings.DownloadOptions)
            {
                if(fileTypes.TryGetValue(option, out var xpath))
                {
                    var nodes = postHtmlDoc.DocumentNode.SelectNodes(xpath);
                    if (nodes != null)
                    {
                        foreach (var node in nodes)
                        {
                            var fileUrl = node.GetAttributeValue("href", "not found");
                            var fileName = node.GetAttributeValue("download", Path.GetFileName(new Uri(fileUrl).LocalPath));

                            if (fileUrl != "not found")
                            {
                                fileTasks.Add(Task.Run(async () =>
                                {
                                    await contentSemaphore.WaitAsync();
                                    try 
                                    {
                                        var fileSizeInMB = await DownloadFileAsync(fileUrl, postSaveDirectory, fileName);
                                        
                                        if (recentDownloads.Count >= heightOfSemiscreenPanel)
                                            recentDownloads.TryDequeue(out _);

                                        string fileSizeText = fileSizeInMB.HasValue ? $"{fileSizeInMB.Value:F2} МБ" : "";
                                        recentDownloads.Enqueue((fileName, fileSizeText));

                                        var tableFiles = new Table()
                                            .AddColumn(new TableColumn(string.Empty))
                                            .AddColumn(new TableColumn(string.Empty).RightAligned())
                                            .Border(TableBorder.None)
                                            .Expand()
                                            .BorderColor(colorMain);
    
                                        foreach (var item in recentDownloads.Take(heightOfSemiscreenPanel))
                                        {
                                            tableFiles.AddRow($"{markupSubMain}{item.FileName}[/]", $"{markupSub2}{item.FileSize}[/]");
                                        }

                                        layout["BottomLeft"].Update(new Panel(tableFiles)
                                            .Header($"{markupMain} Загрузка [/]", Justify.Center)
                                            .BorderColor(colorMain)
                                            .Expand());

                                        lock (lockObject)
                                        {
                                            totalDownloadedFiles++;
                                            if (fileSizeInMB.HasValue)
                                                totalDownloadedSizeInMB += fileSizeInMB.Value;
                                        }


                                        ctx.Refresh(); 
                                    }
                                    finally { contentSemaphore.Release(); }
                                }));
                            }
                        }
                    }
                }
            }

            await Task.WhenAll(fileTasks);
        }

        static string Sanitize(string path)
        {
            var invalidChars = new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };
            foreach (var c in invalidChars)
            {
                path = path.Replace(c, '_');
            }
            return path;
        }


        static async Task<double?> DownloadFileAsync(string url, string saveDir, string fileName)
        {
            string fileSavePath = Path.Combine(saveDir, Sanitize(fileName));
            double? fileSizeInMB = null;

            try
            {
                if (!Directory.Exists(saveDir))
                    Directory.CreateDirectory(saveDir);

                using (var response = await httpClient.GetAsync(url))
                {
                    response.EnsureSuccessStatusCode();

                    await using (var fileStream = new FileStream(fileSavePath, FileMode.Create, FileAccess.Write, FileShare.None))
                    await using (var httpStream = await response.Content.ReadAsStreamAsync())
                    {
                        await httpStream.CopyToAsync(fileStream);
                    }
                }

                var fileInfo = new FileInfo(fileSavePath);
                fileSizeInMB = fileInfo.Length / (1024.0 * 1024.0);

            }
            catch (HttpRequestException e)
            {
                errorQueue.Enqueue($"[red]Ошибка скачивания файла с URL {url}: {e.Message}[/]");
                UpdateErrorPanelAction?.Invoke();
            }
            catch (IOException e)
            {
                errorQueue.Enqueue($"[red]Ошибка записи файла {fileName}: {e.Message}[/]");
                UpdateErrorPanelAction?.Invoke();
            }
            catch (Exception e)
            {
                errorQueue.Enqueue($"[red]Непредвиденная ошибка при скачивании файла {fileName}: {e.Message}[/]");
                UpdateErrorPanelAction?.Invoke();
            }

            return fileSizeInMB;
        }


        static async Task<string> GetHtmlAsync(string url)
        {
            const int maxTries = 5;
            int attemp = 0;

            while(attemp < maxTries)
            {
                await requestSemaphore.WaitAsync();

                try
                {
                    var response = await httpClient.GetAsync(url);

                    if (response.StatusCode == (HttpStatusCode)429)
                    {
                        var retryAfter = response.Headers.RetryAfter?.Delta?.TotalSeconds ?? 5;
                        errorQueue.Enqueue($"[red]Превышен лимит запросов {url}.\nОжидание {retryAfter} секунд...[/]");
                        UpdateErrorPanelAction?.Invoke();
                        await Task.Delay(TimeSpan.FromSeconds(retryAfter + random.Next(2)));
                        attemp++;
                        continue;
                    }

                    response.EnsureSuccessStatusCode();
                    var hrml = await response.Content.ReadAsStringAsync();
                    return hrml;
                }
                catch (TaskCanceledException e) when (e.CancellationToken == default)
                {
                    errorQueue.Enqueue($"[red]Тайм-аут запроса для {url}.\nПовторная попытка...[/]");
                    UpdateErrorPanelAction?.Invoke();
                }
                catch (HttpRequestException e)
                {
                    errorQueue.Enqueue($"[red]Ошибка получения HTML для {url}.\nПовторная попытка...[/]");
                    UpdateErrorPanelAction?.Invoke();

                    await Task.Delay(200 + random.Next(100));
                    attemp++;
                    
                }
                finally
                {
                    requestSemaphore.Release();
                }
            }

            return null;
        }

        static async Task<bool> CheckSiteConnection(string baseUrl)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Head, baseUrl);
                var response = await httpClient.SendAsync(request);
                return response.IsSuccessStatusCode;
            }
            catch (HttpRequestException e)
            {
                return false;
            }
            catch (TaskCanceledException e)
            {
                return false;
            }
        }

        static void LoadSettings()
        {
            if (File.Exists("appsettings.json"))
            {
                var json = File.ReadAllText("appsettings.json");
                appSettings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }

        static void SaveSettings()
        {
            var json = JsonSerializer.Serialize(appSettings, new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping});
            File.WriteAllText("appsettings.json", json);
        }

        static void InitializeSettings()
        {
            string appDirectory = Directory.GetCurrentDirectory();
            string authorFilePath = Path.Combine(appDirectory, "author.txt");
            string proxy = "Отсутствует";

            appSettings.SaveDirectoryPath = appDirectory;
            appSettings.AuthorFilePath = authorFilePath;
            appSettings.DownloadOptions = new List<string>
            {
                "Изображения",
                "Архивы",
                "Видео"
            };
            appSettings.ProxyAdress = proxy;
            if (!File.Exists(authorFilePath))
                File.Create(authorFilePath).Dispose();


            SaveSettings();
        }

        private static void UpdateErrorPanel(LiveDisplayContext ctx, Layout layout)
        {
            while (errorQueue.Count > heightOfSemiscreenPanel)
                errorQueue.TryDequeue(out _);

            var errorMessages = string.Join(Environment.NewLine, errorQueue.Take(heightOfSemiscreenPanel));

            var errorPanel = new Panel(errorMessages)
                .Header($"[red1] Ошибки [/]", Justify.Center)
                .BorderColor(Color.Red1)
                .Expand();

            layout["BottomRight"].Update(errorPanel);
            ctx.Refresh();
        }
    }
}