using System.Diagnostics;
using Microsoft.Win32;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;

namespace BackMan.App;

static class Program
{
    private static NotifyIcon? _trayIcon;
    private static System.Windows.Forms.Timer _schedulerTimer = new();
    private static List<ScheduledTask> _tasks = new();
    private static string _configPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "BackMan", "tasks.json");

    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        
        // Ensure config directory exists
        Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
        
        // Load tasks before starting scheduler
        LoadTasks();

        // Run startup tasks immediately
        RunStartupTasks();
        
        // Start the background scheduler
        StartScheduler();
        
        // Create system tray interface
        CreateTrayIcon();
        
        // Register for startup (silent - no errors if fails)
        RegisterStartup();
        
        // Run the application
        Application.Run();
        
        // Cleanup on exit
        _schedulerTimer.Stop();
        _trayIcon?.Dispose();
    }

    static void RegisterStartup()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            key?.SetValue("BackMan", Application.ExecutablePath);
        }
        catch { /* Silent fail - don't bother user */ }
    }

    static void StartScheduler()
    {
        _schedulerTimer.Interval = 30000; // Check every 30 seconds
        _schedulerTimer.Tick += (s, e) => CheckAndRunTasks();
        _schedulerTimer.Start();
    }

    static void LoadTasks()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                var options = new JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true,
                    Converters = { new JsonStringEnumConverter() }
                };
                var config = JsonSerializer.Deserialize<BackManConfig>(json, options);
                _tasks = config?.Tasks ?? new List<ScheduledTask>();
                
                // Initialize ALL enabled tasks to run immediately if they haven't run
                foreach (var task in _tasks.Where(t => t.IsEnabled))
                {
                    if (task.NextRun == DateTime.MinValue)
                    {
                        task.NextRun = DateTime.Now; // Run immediately
                    }
                    else if (task.NextRun < DateTime.Now && task.ScheduleType != ScheduleType.Startup)
                    {
                        task.NextRun = DateTime.Now; // Run immediately if missed
                    }
                }
                SaveConfig(); // Save the updated next run times
            }
            else
            {
                // Create default config
                _tasks = new List<ScheduledTask>
                {
                    new ScheduledTask
                    {
                        Name = "Example Task - Edit Me",
                        ProgramPath = "notepad.exe",
                        ScheduleType = ScheduleType.Startup,
                        IsEnabled = true
                    }
                };
                SaveConfig();
            }
        }
        catch (Exception ex)
        { 
            Debug.WriteLine($"Error loading tasks: {ex.Message}");
            _tasks = new List<ScheduledTask>();
        }
    }

    // static void LoadTasks()
    // {
    //     try
    //     {
    //         if (File.Exists(_configPath))
    //         {
    //             var json = File.ReadAllText(_configPath);
    //             var options = new JsonSerializerOptions 
    //             { 
    //                 PropertyNameCaseInsensitive = true,
    //                 Converters = { new JsonStringEnumConverter() }
    //             };
    //             var config = JsonSerializer.Deserialize<BackManConfig>(json, options);
    //             _tasks = config?.Tasks ?? new List<ScheduledTask>();
                
    //             // Initialize next run times for tasks that haven't been set
    //             foreach (var task in _tasks.Where(t => t.IsEnabled && t.NextRun == DateTime.MinValue))
    //             {
    //                 task.NextRun = CalculateNextRun(task);
    //             }
    //         }
    //         else
    //         {
    //             // Create default config
    //             _tasks = new List<ScheduledTask>
    //             {
    //                 new ScheduledTask
    //                 {
    //                     Name = "Example Task - Edit Me",
    //                     ProgramPath = "notepad.exe",
    //                     ScheduleType = ScheduleType.Startup,
    //                     IsEnabled = true
    //                 }
    //             };
    //             SaveConfig();
    //         }
    //     }
    //     catch (Exception ex)
    //     { 
    //         Debug.WriteLine($"Error loading tasks: {ex.Message}");
    //         _tasks = new List<ScheduledTask>();
    //     }
    // }

    // static void LoadTasks()
    // {
    //     try
    //     {
    //         if (File.Exists(_configPath))
    //         {
    //             var json = File.ReadAllText(_configPath);
    //             var options = new JsonSerializerOptions 
    //             { 
    //                 PropertyNameCaseInsensitive = true,
    //                 Converters = { new JsonStringEnumConverter() }
    //             };
    //             var config = JsonSerializer.Deserialize<BackManConfig>(json, options);
    //             _tasks = config?.Tasks ?? new List<ScheduledTask>();
    //         }
    //         else
    //         {
    //             // Create default config with example task
    //             _tasks = new List<ScheduledTask>
    //             {
    //                 new ScheduledTask
    //                 {
    //                     Name = "Example Task - Edit Me",
    //                     ProgramPath = "notepad.exe",
    //                     ScheduleType = ScheduleType.Startup,
    //                     NextRun = DateTime.Now.AddMinutes(1),
    //                     IsEnabled = true
    //                 }
    //             };
    //             SaveConfig();
    //         }
    //     }
    //     catch 
    //     { 
    //         _tasks = new List<ScheduledTask>();
    //     }
    // }

    static async void CheckAndRunTasks()
    {
        Console.WriteLine($"Checking tasks at {DateTime.Now}");
        
        // Exclude startup tasks from normal scheduler - they only run on app start
        var dueTasks = _tasks.Where(t => t.IsDue && t.ScheduleType != ScheduleType.Startup).ToList();
        
        // Debug output for all enabled tasks
        foreach (var task in _tasks.Where(t => t.IsEnabled))
        {
            Console.WriteLine($"Task '{task.Name}': NextRun={task.NextRun}, IsDue={task.IsDue}, Now={DateTime.Now}");
        }
        
        Console.WriteLine($"Found {dueTasks.Count} due tasks");
        
        // Separate admin and non-admin tasks
        var adminTasks = dueTasks.Where(t => t.RunAsAdmin).ToList();
        var nonAdminTasks = dueTasks.Where(t => !t.RunAsAdmin).ToList();
        
        // Run non-admin tasks first
        foreach (var task in nonAdminTasks)
        {
            Debug.WriteLine($"Running task: {task.Name}");
            Console.WriteLine($"Executing task: {task.Name}");
            ExecuteTask(task);
            UpdateTaskSchedule(task);
        }
        
        // Run admin tasks with delays between them
        foreach (var task in adminTasks)
        {
            Debug.WriteLine($"Running admin task: {task.Name}");
            Console.WriteLine($"Executing admin task: {task.Name}");
            await ExecuteAdminTaskWithDelay(task);
            UpdateTaskSchedule(task);
        }
        
        // Update tray icon status
        UpdateTrayStatus();
    }

    // static void CheckAndRunTasks()
    // {
    //     Console.WriteLine($"Checking tasks at {DateTime.Now}");
        
    //     // Exclude startup tasks from normal scheduler - they only run on app start
    //     var dueTasks = _tasks.Where(t => t.IsDue && t.ScheduleType != ScheduleType.Startup).ToList();
        
    //     // Debug output for all enabled tasks
    //     foreach (var task in _tasks.Where(t => t.IsEnabled))
    //     {
    //         Console.WriteLine($"Task '{task.Name}': NextRun={task.NextRun}, IsDue={task.IsDue}, Now={DateTime.Now}");
    //     }
        
    //     Console.WriteLine($"Found {dueTasks.Count} due tasks");
        
    //     foreach (var task in dueTasks)
    //     {
    //         Debug.WriteLine($"Running task: {task.Name}");
    //         Console.WriteLine($"Executing task: {task.Name}");
    //         ExecuteTask(task);
    //         UpdateTaskSchedule(task);
    //     }
        
    //     // Update tray icon status
    //     UpdateTrayStatus();
    // }

    // static void CheckAndRunTasks()
    // {
    //     Console.WriteLine($"Checking tasks at {DateTime.Now}");
        
    //     var dueTasks = _tasks.Where(t => t.IsDue).ToList();
        
    //     // Debug output for all enabled tasks
    //     foreach (var task in _tasks.Where(t => t.IsEnabled))
    //     {
    //         Console.WriteLine($"Task '{task.Name}': NextRun={task.NextRun}, IsDue={task.IsDue}, Now={DateTime.Now}");
    //     }
        
    //     Console.WriteLine($"Found {dueTasks.Count} due tasks");
        
    //     foreach (var task in dueTasks)
    //     {
    //         Debug.WriteLine($"Running task: {task.Name}");
    //         Console.WriteLine($"Executing task: {task.Name}");
    //         ExecuteTask(task);
    //         UpdateTaskSchedule(task);
    //     }
        
    //     // Update tray icon status
    //     UpdateTrayStatus();
    // }

    static async void RunStartupTasks()
    {
        var startupTasks = _tasks.Where(t => t.IsEnabled && t.ScheduleType == ScheduleType.Startup).ToList();
        
        Console.WriteLine($"Running {startupTasks.Count} startup tasks");
        
        foreach (var task in startupTasks)
        {
            Debug.WriteLine($"Running startup task: {task.Name}");
            Console.WriteLine($"Executing startup task: {task.Name}");
            
            if (task.RunAsAdmin)
            {
                // For admin tasks, add a delay between them to allow separate UAC prompts
                await ExecuteAdminTaskWithDelay(task);
            }
            else
            {
                ExecuteTask(task);
            }
            
            // Update task status but don't reschedule - they run on every app start
            task.LastRun = DateTime.Now;
            task.NextRun = DateTime.MaxValue;
        }
        SaveConfig();
    }

    static async Task ExecuteAdminTaskWithDelay(ScheduledTask task)
    {
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = task.ProgramPath,
                Arguments = task.Arguments ?? "",
                WorkingDirectory = Path.GetDirectoryName(task.ProgramPath) ?? Environment.CurrentDirectory,
                Verb = "runas",
                UseShellExecute = true,
                WindowStyle = task.StartMinimized ? ProcessWindowStyle.Minimized : ProcessWindowStyle.Normal
            };

            Process.Start(processInfo);
            
            // Wait 3 seconds between admin tasks to allow separate UAC prompts
            await Task.Delay(3000);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to execute admin task {task.Name}: {ex.Message}");
            Console.WriteLine($"ERROR executing admin task {task.Name}: {ex.Message}");
        }
    }

    // static void RunStartupTasks()
    // {
    //     var startupTasks = _tasks.Where(t => t.IsEnabled && t.ScheduleType == ScheduleType.Startup).ToList();
        
    //     Console.WriteLine($"Running {startupTasks.Count} startup tasks");
        
    //     foreach (var task in startupTasks)
    //     {
    //         Debug.WriteLine($"Running startup task: {task.Name}");
    //         Console.WriteLine($"Executing startup task: {task.Name}");
    //         ExecuteTask(task);
    //         // Update task status but don't reschedule - they run on every app start
    //         task.LastRun = DateTime.Now;
    //         task.NextRun = DateTime.MaxValue;
    //     }
    //     SaveConfig();
    // }

    // static void RunStartupTasks()
    // {
    //     var startupTasks = _tasks.Where(t => t.IsEnabled && t.ScheduleType == ScheduleType.Startup).ToList();
        
    //     foreach (var task in startupTasks)
    //     {
    //         Debug.WriteLine($"Running startup task: {task.Name}");
    //         Console.WriteLine($"Executing startup task: {task.Name}");
    //         ExecuteTask(task);
    //         // Don't update schedule for startup tasks - they run on every app start
    //     }
    // }

    // static void CheckAndRunTasks()
    // {
    //     var dueTasks = _tasks.Where(t => t.IsDue).ToList();
        
    //     foreach (var task in dueTasks)
    //     {
    //         Debug.WriteLine($"Running task: {task.Name}");
    //         ExecuteTask(task);
    //         UpdateTaskSchedule(task);
    //     }
        
    //     // Update tray icon status
    //     UpdateTrayStatus();
    // }

    // static void CheckAndRunTasks()
    // {
    //     var dueTasks = _tasks.Where(t => t.IsDue).ToList();
        
    //     foreach (var task in dueTasks)
    //     {
    //         ExecuteTask(task);
    //         UpdateTaskSchedule(task);
    //     }
        
    //     // Update tray icon status
    //     UpdateTrayStatus();
    // }

    static void ExecuteTask(ScheduledTask task)
    {
        try
        {
            if (task.RunAsAdmin)
            {
                // Use a different approach for admin tasks to avoid UAC grouping
                ExecuteAsAdmin(task);
            }
            else
            {
                ExecuteAsUser(task);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to execute task {task.Name}: {ex.Message}");
            Console.WriteLine($"ERROR executing {task.Name}: {ex.Message}");
        }
    }

    static void ExecuteAsUser(ScheduledTask task)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = task.ProgramPath,
            Arguments = task.Arguments ?? "",
            WorkingDirectory = Path.GetDirectoryName(task.ProgramPath) ?? Environment.CurrentDirectory
        };

        if (task.RunInBackground)
        {
            processInfo.WindowStyle = ProcessWindowStyle.Hidden;
            processInfo.CreateNoWindow = true;
            processInfo.UseShellExecute = false;
        }
        else
        {
            processInfo.WindowStyle = task.StartMinimized ? ProcessWindowStyle.Minimized : ProcessWindowStyle.Normal;
            processInfo.UseShellExecute = true;
        }

        Process.Start(processInfo);
    }

    static void ExecuteAsAdmin(ScheduledTask task)
    {
        try
        {
            string fileName = task.ProgramPath;
            string arguments = task.Arguments ?? "";
            
            // ALWAYS run batch files through cmd.exe when admin rights are needed
            if (task.Type == TaskType.Batch || fileName.ToLower().EndsWith(".bat"))
            {
                fileName = "cmd.exe";
                arguments = $"/c \"{fileName}\"";
            }

            var processInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = Path.GetDirectoryName(task.ProgramPath) ?? Environment.CurrentDirectory,
                Verb = "runas",
                UseShellExecute = true,
                WindowStyle = task.StartMinimized ? ProcessWindowStyle.Minimized : ProcessWindowStyle.Normal
            };

            Process.Start(processInfo);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to execute admin task {task.Name}: {ex.Message}");
            Console.WriteLine($"ERROR executing admin task {task.Name}: {ex.Message}");
        }
    }

    // static void ExecuteAsAdmin(ScheduledTask task)
    // {
    //     // For batch files, run them through cmd.exe with admin rights
    //     string fileName = task.ProgramPath;
    //     string arguments = task.Arguments ?? "";
        
    //     if (task.Type == TaskType.Batch && !fileName.ToLower().EndsWith(".exe"))
    //     {
    //         fileName = "cmd.exe";
    //         arguments = $"/c \"{task.ProgramPath}\" {arguments}";
    //     }

    //     var processInfo = new ProcessStartInfo
    //     {
    //         FileName = fileName,
    //         Arguments = arguments,
    //         WorkingDirectory = Path.GetDirectoryName(task.ProgramPath) ?? Environment.CurrentDirectory,
    //         Verb = "runas",
    //         UseShellExecute = true,
    //         WindowStyle = task.StartMinimized ? ProcessWindowStyle.Minimized : ProcessWindowStyle.Normal
    //     };

    //     Process.Start(processInfo);
    // }

    // static void ExecuteTask(ScheduledTask task)
    // {
    //     try
    //     {
    //         var processInfo = new ProcessStartInfo
    //         {
    //             FileName = task.ProgramPath,
    //             Arguments = task.Arguments ?? "",
    //             WorkingDirectory = Path.GetDirectoryName(task.ProgramPath) ?? Environment.CurrentDirectory
    //         };

    //         // Handle admin elevation FIRST - it overrides other settings
    //         if (task.RunAsAdmin)
    //         {
    //             processInfo.Verb = "runas";
    //             processInfo.UseShellExecute = true; // Required for runas
    //             processInfo.WindowStyle = task.StartMinimized ? ProcessWindowStyle.Minimized : ProcessWindowStyle.Normal;
    //         }
    //         else
    //         {
    //             // Handle window state for non-admin tasks
    //             if (task.RunInBackground)
    //             {
    //                 processInfo.WindowStyle = ProcessWindowStyle.Hidden;
    //                 processInfo.CreateNoWindow = true;
    //                 processInfo.UseShellExecute = false;
    //             }
    //             else
    //             {
    //                 processInfo.WindowStyle = task.StartMinimized ? ProcessWindowStyle.Minimized : ProcessWindowStyle.Normal;
    //                 processInfo.UseShellExecute = true;
    //             }
    //         }

    //         Process.Start(processInfo);
    //     }
    //     catch (Exception ex)
    //     {
    //         Debug.WriteLine($"Failed to execute task {task.Name}: {ex.Message}");
    //         Console.WriteLine($"ERROR executing {task.Name}: {ex.Message}");
    //     }
    // }

    // static void ExecuteTask(ScheduledTask task)
    // {
    //     try
    //     {
    //         var processInfo = new ProcessStartInfo
    //         {
    //             FileName = task.ProgramPath,
    //             Arguments = task.Arguments ?? "",
    //             WorkingDirectory = Path.GetDirectoryName(task.ProgramPath) ?? Environment.CurrentDirectory
    //         };

    //         // Handle window state
    //         if (task.RunInBackground)
    //         {
    //             processInfo.WindowStyle = ProcessWindowStyle.Hidden;
    //             processInfo.CreateNoWindow = true;
    //             processInfo.UseShellExecute = false;
    //         }
    //         else
    //         {
    //             processInfo.WindowStyle = task.StartMinimized ? ProcessWindowStyle.Minimized : ProcessWindowStyle.Normal;
    //             processInfo.UseShellExecute = true;
    //         }

    //         // Handle admin elevation
    //         if (task.RunAsAdmin)
    //         {
    //             processInfo.Verb = "runas"; // This triggers UAC prompt
    //             processInfo.UseShellExecute = true; // Required for admin elevation
    //         }

    //         Process.Start(processInfo);
    //     }
    //     catch (Exception ex)
    //     {
    //         Debug.WriteLine($"Failed to execute task {task.Name}: {ex.Message}");
    //     }
    // }

    // static void ExecuteTask(ScheduledTask task)
    // {
    //     try
    //     {
    //         var processInfo = new ProcessStartInfo
    //         {
    //             FileName = task.ProgramPath,
    //             Arguments = task.Arguments ?? "",
    //             WindowStyle = task.StartMinimized ? ProcessWindowStyle.Minimized : ProcessWindowStyle.Normal,
    //             CreateNoWindow = task.RunInBackground,
    //             UseShellExecute = !task.RunInBackground,
    //             WorkingDirectory = Path.GetDirectoryName(task.ProgramPath) ?? Environment.CurrentDirectory
    //         };
            
    //         Process.Start(processInfo);
    //     }
    //     catch (Exception ex)
    //     {
    //         // Log to debug output - silent failure
    //         Debug.WriteLine($"Failed to execute task {task.Name}: {ex.Message}");
    //     }
    // }

    static void UpdateTaskSchedule(ScheduledTask task)
    {
        task.LastRun = DateTime.Now;
        
        // For startup tasks, don't set a future NextRun - they'll run on next app start
        if (task.ScheduleType == ScheduleType.Startup)
        {
            task.NextRun = DateTime.MaxValue; // Won't run again until app restart
        }
        else
        {
            task.NextRun = CalculateNextRun(task);
        }
        
        SaveConfig();
    }

    // static void UpdateTaskSchedule(ScheduledTask task)
    // {
    //     task.LastRun = DateTime.Now;
    //     task.NextRun = CalculateNextRun(task);
    //     SaveConfig();
    // }

    // static DateTime CalculateNextRun(ScheduledTask task)
    // {
    //     var now = DateTime.Now;
    //     var today = DateTime.Today;
        
    //     // If task has never run and NextRun is MinValue, calculate proper next run
    //     if (task.LastRun == DateTime.MinValue && task.NextRun == DateTime.MinValue)
    //     {
    //         return CalculateFirstRun(task, now, today);
    //     }
        
    //     return task.ScheduleType switch
    //     {
    //         ScheduleType.Startup => DateTime.MaxValue, // Run once only
    //         ScheduleType.Daily => CalculateDailyNextRun(task, now, today),
    //         ScheduleType.Weekly => CalculateWeeklyNextRun(task, now, today),
    //         ScheduleType.Monthly => CalculateMonthlyNextRun(task, now, today),
    //         ScheduleType.Interval when task.Interval.HasValue => now.Add(task.Interval.Value),
    //         _ => DateTime.MaxValue
    //     };
    // }

    static DateTime CalculateFirstRun(ScheduledTask task, DateTime now, DateTime today)
    {
        // For startup tasks, run immediately
        if (task.ScheduleType == ScheduleType.Startup)
            return now;
            
        // For scheduled tasks with specific time, calculate next occurrence
        var scheduledTimeToday = today.Add(task.ScheduledTime);
        
        if (scheduledTimeToday > now)
            return scheduledTimeToday; // Run later today
        else
            return task.ScheduleType switch
            {
                ScheduleType.Daily => scheduledTimeToday.AddDays(1),
                ScheduleType.Weekly => scheduledTimeToday.AddDays(7),
                ScheduleType.Monthly => scheduledTimeToday.AddMonths(1),
                _ => scheduledTimeToday.AddDays(1)
            };
    }

    static DateTime CalculateNextRun(ScheduledTask task)
    {
        var now = DateTime.Now;
        var today = DateTime.Today;
        
        // If task has never run properly, run immediately
        if (task.LastRun == DateTime.MinValue && task.NextRun == DateTime.MinValue)
        {
            return now;
        }
        
        return task.ScheduleType switch
        {
            ScheduleType.Startup => DateTime.MaxValue, // Don't auto-schedule - only run on app start
            ScheduleType.Daily => CalculateDailyNextRun(task, now, today),
            ScheduleType.Weekly => CalculateWeeklyNextRun(task, now, today),
            ScheduleType.Monthly => CalculateMonthlyNextRun(task, now, today),
            ScheduleType.Interval when task.Interval.HasValue => now.Add(task.Interval.Value),
            _ => DateTime.MaxValue
        };
    }

    static DateTime CalculateDailyNextRun(ScheduledTask task, DateTime now, DateTime today)
    {
        var scheduledTimeToday = today.Add(task.ScheduledTime);
        return scheduledTimeToday <= now ? now : scheduledTimeToday;
    }

    static DateTime CalculateWeeklyNextRun(ScheduledTask task, DateTime now, DateTime today)
    {
        // Find next Monday (or today if it's Monday and time hasn't passed)
        int daysUntilMonday = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        var nextMonday = today.AddDays(daysUntilMonday);
        var scheduledTime = nextMonday.Add(task.ScheduledTime);
        
        return scheduledTime <= now ? now : scheduledTime;
    }

    static DateTime CalculateMonthlyNextRun(ScheduledTask task, DateTime now, DateTime today)
    {
        // Check if we can run on the 1st of current month
        var firstOfThisMonth = new DateTime(today.Year, today.Month, 1);
        var scheduledTimeThisMonth = firstOfThisMonth.Add(task.ScheduledTime);
        
        // If we haven't passed the 1st of this month yet, run then
        if (today.Day == 1 && now.TimeOfDay <= task.ScheduledTime)
        {
            return scheduledTimeThisMonth;
        }
        
        // Otherwise, run immediately (we're past the 1st) or on 1st of next month
        var firstOfNextMonth = firstOfThisMonth.AddMonths(1);
        var scheduledTimeNextMonth = firstOfNextMonth.Add(task.ScheduledTime);
        
        return scheduledTimeNextMonth <= now ? now : scheduledTimeNextMonth;
    }

    // static DateTime CalculateDailyNextRun(ScheduledTask task, DateTime now, DateTime today)
    // {
    //     var nextRun = today.Add(task.ScheduledTime);
    //     if (nextRun <= now)
    //         nextRun = nextRun.AddDays(1);
    //     return nextRun;
    // }

    // static DateTime CalculateWeeklyNextRun(ScheduledTask task, DateTime now, DateTime today)
    // {
    //     var nextRun = today.Add(task.ScheduledTime);
    //     if (nextRun <= now)
    //         nextRun = nextRun.AddDays(7);
    //     return nextRun;
    // }

    // static DateTime CalculateMonthlyNextRun(ScheduledTask task, DateTime now, DateTime today)
    // {
    //     // For monthly tasks, run on the 1st of next month at scheduled time
    //     var firstOfNextMonth = new DateTime(today.Year, today.Month, 1).AddMonths(1);
    //     var nextRun = firstOfNextMonth.Add(task.ScheduledTime);
        
    //     // If we're already past the 1st of this month, schedule for next month
    //     if (today.Day > 1 || (today.Day == 1 && now.TimeOfDay > task.ScheduledTime))
    //     {
    //         return nextRun;
    //     }
        
    //     // Otherwise, run on the 1st of current month if not past scheduled time
    //     var firstOfThisMonth = new DateTime(today.Year, today.Month, 1).Add(task.ScheduledTime);
    //     return firstOfThisMonth >= now ? firstOfThisMonth : nextRun;
    // }

    // static DateTime CalculateMonthlyNextRun(ScheduledTask task, DateTime now, DateTime today)
    // {
    //     var nextRun = today.Add(task.ScheduledTime);
    //     if (nextRun <= now)
    //         nextRun = nextRun.AddMonths(1);
    //     return nextRun;
    // }

    // static DateTime CalculateNextRun(ScheduledTask task)
    // {
    //     var now = DateTime.Now;
    //     var today = DateTime.Today;
        
    //     // If this is a first-time run and scheduled time has passed, run immediately
    //     if (task.LastRun == DateTime.MinValue)
    //     {
    //         var scheduledTime = today.Add(task.ScheduledTime);
    //         if (scheduledTime < now)
    //             return now; // Run immediately if overdue
    //     }
        
    //     return task.ScheduleType switch
    //     {
    //         ScheduleType.Startup => DateTime.MaxValue,
    //         ScheduleType.Daily => CalculateDaily(task, now, today),
    //         ScheduleType.Weekly => CalculateWeekly(task, now, today),
    //         ScheduleType.Monthly => CalculateMonthly(task, now, today),
    //         ScheduleType.Interval when task.Interval.HasValue => now.Add(task.Interval.Value),
    //         _ => now.AddDays(1)
    //     };
    // }

    // static DateTime CalculateDaily(ScheduledTask task, DateTime now, DateTime today)
    // {
    //     var scheduledTime = today.Add(task.ScheduledTime);
    //     return scheduledTime < now ? now : scheduledTime; // Run now if overdue
    // }

    // static DateTime CalculateWeekly(ScheduledTask task, DateTime now, DateTime today)
    // {
    //     var scheduledTime = today.Add(task.ScheduledTime);
    //     return scheduledTime < now ? now : scheduledTime; // Run now if overdue
    // }

    // static DateTime CalculateMonthly(ScheduledTask task, DateTime now, DateTime today)
    // {
    //     var scheduledTime = today.Add(task.ScheduledTime);
    //     return scheduledTime < now ? now : scheduledTime; // Run now if overdue
    // }

//     static DateTime CalculateNextRun(ScheduledTask task)
// {
//     var now = DateTime.Now;
    
//     switch (task.ScheduleType)
//     {
//         case ScheduleType.Startup:
//             return DateTime.MaxValue; // Run once
            
//         case ScheduleType.Daily:
//             var dailyTime = DateTime.Today.Add(task.ScheduledTime);
//             return dailyTime < now ? dailyTime.AddDays(1) : dailyTime;
            
//         case ScheduleType.Weekly:
//             // Simple weekly - run same time next week
//             return now.AddDays(7);
            
//         case ScheduleType.Monthly:
//             // Simple monthly - run same time next month  
//             return now.AddMonths(1);
            
//         case ScheduleType.Interval when task.Interval.HasValue:
//             return now.Add(task.Interval.Value);
            
//         default:
//             return now.AddDays(1);
//     }
// }

    // static DateTime CalculateNextRun(ScheduledTask task)
    // {
    //     var now = DateTime.Now;
        
    //     return task.ScheduleType switch
    //     {
    //         ScheduleType.Startup => DateTime.MaxValue, // Run once
    //         ScheduleType.Daily => now.AddDays(1),
    //         ScheduleType.Interval when task.Interval.HasValue => now.Add(task.Interval.Value),
    //         _ => now.AddDays(1) // Default fallback
    //     };
    // }

    static void SaveConfig()
    {
        try
        {
            var config = new BackManConfig { Tasks = _tasks };
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(config, options);
            File.WriteAllText(_configPath, json);
        }
        catch { /* Silent save failure */ }
    }

    static void CreateTrayIcon()
    {
        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "BackMan - Loading...",
            Visible = true
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Show Tasks", null, ShowTasks);
        menu.Items.Add("Edit Config", null, EditConfig);
        menu.Items.Add("Restart Scheduler", null, RestartScheduler);
        menu.Items.Add("Exit", null, ExitApp);
        
        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += ShowTasks;
        
        UpdateTrayStatus();
    }

    static void UpdateTrayStatus()
    {
        if (_trayIcon != null)
        {
            var enabledCount = _tasks.Count(t => t.IsEnabled);
            var dueCount = _tasks.Count(t => t.IsDue);
            _trayIcon.Text = $"BackMan - {enabledCount} tasks ({dueCount} due)";
        }
    }

    static void ShowTasks(object? sender, EventArgs e)
    {
        var taskList = _tasks.Select(t => 
            $"{t.Name}: {t.NextRun:g} {(t.IsEnabled ? "" : "(Disabled)")}"
        ).ToArray();
        
        MessageBox.Show(
            string.Join("\n", taskList) + "\n\nConfig: " + _configPath,
            $"BackMan - {_tasks.Count} Tasks",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information
        );
    }

    static void EditConfig(object? sender, EventArgs e)
    {
        try
        {
            Process.Start("notepad.exe", _configPath);
            
            // Reload config after a delay in case user edits it
            Task.Delay(5000).ContinueWith(_ => 
            {
                LoadTasks();
                UpdateTrayStatus();
            });
        }
        catch
        {
            MessageBox.Show($"Could not open config file:\n{_configPath}", "BackMan Error", 
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    static void RestartScheduler(object? sender, EventArgs e)
    {
        _schedulerTimer.Stop();
        LoadTasks();
        _schedulerTimer.Start();
        UpdateTrayStatus();
        MessageBox.Show("Scheduler restarted", "BackMan", 
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    static void ExitApp(object? sender, EventArgs e)
    {
        _schedulerTimer.Stop();
        _trayIcon?.Visible = false;
        Application.Exit();
    }
}

// Core data models
public class BackManConfig
{
    public List<ScheduledTask> Tasks { get; set; } = new();
}

public class ScheduledTask
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ProgramPath { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public TaskType Type { get; set; } = TaskType.Program;
    public bool StartMinimized { get; set; }
    public bool RunInBackground { get; set; }
    public bool RunAsAdmin { get; set; }
    public ScheduleType ScheduleType { get; set; }
    public TimeSpan? Interval { get; set; }
    public TimeSpan ScheduledTime { get; set; }
    public DateTime NextRun { get; set; }
    public DateTime LastRun { get; set; }
    public bool IsEnabled { get; set; } = true;
    
    [JsonIgnore]
    public bool IsDue => IsEnabled && NextRun <= DateTime.Now;
}

public enum TaskType
{
    Program,
    Batch,
    PowerShell
}

public enum ScheduleType
{
    Startup,
    Daily,
    Weekly,
    Monthly,
    Interval
}