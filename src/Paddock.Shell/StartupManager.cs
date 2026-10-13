using System.Security;
using Microsoft.Win32;
using Paddock.Core.Services;

namespace Paddock.Shell;

/// <summary>
/// Manages "Start with Windows".
///
/// The usual way — a value under HKCU\…\Run — is slow on Windows 11: those
/// programs are started one after another, held back while the machine is
/// busy after sign-in, so padx could appear minutes after the desktop. A
/// Task Scheduler task with a logon trigger runs right at sign-in instead.
/// A user can create one for themselves without administrator rights; if that
/// is not allowed (a policy, say), the Run value is used after all.
/// </summary>
public class StartupManager
{
    private const string RegistryKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "padx";

    /// <summary>The entry name used before the app was renamed; removed so it never starts twice.</summary>
    private const string LegacyAppName = "Paddock";

    /// <summary>One task per user, so accounts on a shared PC do not collide.</summary>
    public static string TaskName => $"padx - {Environment.UserName}";

    private const int TASK_CREATE_OR_UPDATE = 6;
    private const int TASK_LOGON_INTERACTIVE_TOKEN = 3;

    /// <summary>
    /// Returns true if padx is configured to start with Windows.
    /// </summary>
    public bool IsStartupEnabled()
        => TaskCommand() is not null || RunValue() is not null;

    /// <summary>
    /// Enables or disables starting padx with Windows. Cheap when nothing
    /// needs to change, so it can be called at every start.
    /// </summary>
    public void SetStartupEnabled(bool enabled)
    {
        var exePath = Environment.ProcessPath ?? string.Empty;

        if (enabled && (string.Equals(TaskCommand(), exePath, StringComparison.OrdinalIgnoreCase) || TryRegisterTask(exePath)))
        {
            // The task starts padx; a Run value as well would only start it late a second time.
            SetRunValue(null);
            return;
        }

        if (!enabled)
            DeleteTask();

        SetRunValue(enabled ? $"\"{exePath}\"" : null);
    }

    // --- Task Scheduler ---

    /// <summary>The program the logon task starts, or null when there is no task.</summary>
    private static string? TaskCommand()
    {
        try
        {
            dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
            service.Connect();
            dynamic task = service.GetFolder("\\").GetTask(TaskName);
            string xml = task.Xml;

            var start = xml.IndexOf("<Command>", StringComparison.Ordinal);
            var end = xml.IndexOf("</Command>", StringComparison.Ordinal);
            return start < 0 || end < start
                ? null
                : System.Net.WebUtility.HtmlDecode(xml[(start + "<Command>".Length)..end]).Trim('"');
        }
        catch (Exception)
        {
            // No such task (or no Task Scheduler at all).
            return null;
        }
    }

    private static bool TryRegisterTask(string exePath)
    {
        try
        {
            var user = $"{Environment.UserDomainName}\\{Environment.UserName}";
            dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
            service.Connect();
            service.GetFolder("\\").RegisterTask(TaskName, TaskXml(exePath, user), TASK_CREATE_OR_UPDATE, null, null, TASK_LOGON_INTERACTIVE_TOKEN);
            Log.Info($"Start with Windows: logon task '{TaskName}' set.");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not create the logon task, using the Run key instead: {ex.Message}");
            return false;
        }
    }

    private static void DeleteTask()
    {
        try
        {
            dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
            service.Connect();
            service.GetFolder("\\").DeleteTask(TaskName, 0);
            Log.Info($"Start with Windows: logon task '{TaskName}' removed.");
        }
        catch (Exception)
        {
            // There was none.
        }
    }

    /// <summary>
    /// At this user's sign-in, start padx: no delay, normal priority (tasks
    /// default to below normal), no time limit (the default stops a task
    /// after three days), on battery too.
    /// </summary>
    private static string TaskXml(string exePath, string user) => $"""
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <RegistrationInfo>
            <Description>Starts padx when you sign in.</Description>
          </RegistrationInfo>
          <Triggers>
            <LogonTrigger>
              <Enabled>true</Enabled>
              <UserId>{SecurityElement.Escape(user)}</UserId>
            </LogonTrigger>
          </Triggers>
          <Principals>
            <Principal id="Author">
              <UserId>{SecurityElement.Escape(user)}</UserId>
              <LogonType>InteractiveToken</LogonType>
              <RunLevel>LeastPrivilege</RunLevel>
            </Principal>
          </Principals>
          <Settings>
            <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
            <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
            <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
            <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
            <Priority>4</Priority>
            <IdleSettings>
              <StopOnIdleEnd>false</StopOnIdleEnd>
            </IdleSettings>
          </Settings>
          <Actions Context="Author">
            <Exec>
              <Command>"{SecurityElement.Escape(exePath)}"</Command>
            </Exec>
          </Actions>
        </Task>
        """;

    // --- Run key (fallback, and clean-up) ---

    private static string? RunValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, false);
        return key?.GetValue(AppName) as string;
    }

    private static void SetRunValue(string? command)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true);
        if (key is null)
            return;

        key.DeleteValue(LegacyAppName, throwOnMissingValue: false);

        if (command is null)
            key.DeleteValue(AppName, throwOnMissingValue: false);
        else
            key.SetValue(AppName, command);
    }
}
