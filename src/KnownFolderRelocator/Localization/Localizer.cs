using System.Globalization;

namespace KnownFolderRelocator.Localization;

public enum AppLanguage
{
    English,
    Chinese
}

public sealed class Localizer
{
    private static readonly Dictionary<string, (string En, string Zh)> Strings = new()
    {
        ["AppTitle"] = ("Known Folder Relocator", "已知文件夹迁移工具"),
        ["MenuVerify"] = ("Verify current known folder paths", "检查当前已知文件夹路径"),
        ["MenuPreviewMigrate"] = ("Preview migration to a target drive/root", "预览迁移到目标盘/目录"),
        ["MenuRunMigrate"] = ("Run migration to a target drive/root", "执行迁移到目标盘/目录"),
        ["MenuPreviewAttach"] = ("Preview re-attach to existing target data", "预览重新连接已有目标数据"),
        ["MenuRunAttach"] = ("Run re-attach to existing target data", "执行重新连接已有目标数据"),
        ["MenuRestore"] = ("Restore from latest or specified state file", "从最新或指定状态文件恢复"),
        ["MenuPreviewCleanup"] = ("Preview cleanup of duplicate old C: files", "预览清理 C 盘旧重复文件"),
        ["MenuRunCleanup"] = ("Run cleanup of duplicate old C: files", "执行清理 C 盘旧重复文件"),
        ["MenuHelp"] = ("Show command-line help", "显示命令行帮助"),
        ["MenuExit"] = ("Exit", "退出"),
        ["SelectOption"] = ("Select an option: ", "请选择操作: "),
        ["UnknownOption"] = ("Unknown option.", "未知选项。"),
        ["Canceled"] = ("Canceled.", "已取消。"),
        ["PressEnter"] = ("Press Enter to continue...", "按 Enter 继续..."),
        ["Name"] = ("Name", "名称"),
        ["Exists"] = ("Exists", "存在"),
        ["Path"] = ("Path", "路径"),
        ["Action"] = ("Action", "操作"),
        ["CurrentToTarget"] = ("Current path -> Target path", "当前路径 -> 目标路径"),
        ["Copy"] = ("Copy", "复制"),
        ["Error"] = ("Error", "错误"),
        ["Skipped"] = ("skipped", "已跳过"),
        ["Unchanged"] = ("Unchanged", "未变化"),
        ["WouldSet"] = ("WouldSet", "将设置"),
        ["Set"] = ("Set", "已设置"),
        ["StateFile"] = ("State file", "状态文件"),
        ["DryRunOnly"] = ("Dry run only. No files or Shell paths were changed.", "仅预览。没有修改文件或 Shell 路径。"),
        ["TargetRoot"] = ("Target root", "目标根目录"),
        ["MigrationConfirm"] = ("This will update Windows known folder paths. Continue?", "这将更新 Windows 已知文件夹路径。是否继续？"),
        ["RestoreConfirm"] = ("This will restore known folder paths from the selected state file. Continue?", "这将从所选状态文件恢复已知文件夹路径。是否继续？"),
        ["CleanupConfirm"] = ("This will delete duplicate old C: files whose target counterpart has the same SHA-256 hash. Continue?", "这将删除目标文件 SHA-256 一致的 C 盘旧重复文件。是否继续？"),
        ["TypeYes"] = ("Type YES to continue: ", "输入 YES 继续: "),
        ["StateFilePrompt"] = ("State file path, or blank for latest: ", "状态文件路径，留空则使用最新状态文件: "),
        ["PreviewOnlyPrompt"] = ("Preview only? [Y/n]: ", "仅预览？[Y/n]: "),
        ["RemoveEmptyDirsPrompt"] = ("Remove empty old directories too? [y/N]: ", "同时删除旧的空目录？[y/N]: "),
        ["TargetDrivePrompt"] = ("Target drive, for example E: (leave blank to enter full target root): ", "目标盘，例如 E:（留空则输入完整目标根目录）: "),
        ["TargetRootPrompt"] = ("Target root, for example E:\\Users\\pyo1024: ", "目标根目录，例如 E:\\Users\\pyo1024: "),
        ["CopyStrategy"] = ("Copy strategy:", "复制策略:"),
        ["CopyStrategy1"] = ("1. CopyMissing (default, do not overwrite target files)", "1. CopyMissing（默认，不覆盖目标文件）"),
        ["CopyStrategy2"] = ("2. NoCopy (only update known folder paths)", "2. NoCopy（只更新已知文件夹路径）"),
        ["CopyStrategy3"] = ("3. BackupConflicts (backup target conflicts, then copy source files)", "3. BackupConflicts（备份目标冲突文件，然后复制源文件）"),
        ["SelectCopyStrategyMigrate"] = ("Select copy strategy [1]: ", "请选择复制策略 [1]: "),
        ["SelectCopyStrategyAttach"] = ("Select copy strategy [2]: ", "请选择复制策略 [2]: "),
        ["InvalidCopyStrategy"] = ("Invalid copy strategy.", "无效的复制策略。"),
        ["RestoreCompleted"] = ("Restore completed from {0}", "已从 {0} 完成恢复"),
        ["RestorePreview"] = ("Restore preview from {0}", "已从 {0} 完成恢复预览"),
        ["CleanupRequiresForce"] = ("cleanup requires --dry-run or --force.", "cleanup 需要 --dry-run 或 --force。"),
        ["UnknownCommand"] = ("Unknown command.", "未知命令。"),
        ["NotWindows"] = ("known-folder-relocator must run on Windows.", "known-folder-relocator 必须在 Windows 上运行。")
    };

    public Localizer(AppLanguage language)
    {
        Language = language;
    }

    public AppLanguage Language { get; }

    public static Localizer Create(string? languageCode = null)
    {
        if (!string.IsNullOrWhiteSpace(languageCode))
        {
            return new Localizer(languageCode.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
                ? AppLanguage.Chinese
                : AppLanguage.English);
        }

        return new Localizer(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase)
            ? AppLanguage.Chinese
            : AppLanguage.English);
    }

    public string T(string key)
    {
        if (!Strings.TryGetValue(key, out var value))
        {
            return key;
        }

        return Language == AppLanguage.Chinese ? value.Zh : value.En;
    }

    public string HelpText => Language == AppLanguage.Chinese ? ChineseHelpText : EnglishHelpText;

    private const string EnglishHelpText = """
    Usage:
      known-folder-relocator [--lang en|zh] verify
      known-folder-relocator [--lang en|zh] migrate --target-drive E [--copy-strategy CopyMissing|NoCopy|BackupConflicts] [--dry-run]
      known-folder-relocator [--lang en|zh] migrate --target-root E:\Users\pyo1024 [--dry-run]
      known-folder-relocator [--lang en|zh] attach --target-drive E [--no-copy] [--dry-run]
      known-folder-relocator [--lang en|zh] restore --state .state\shell-known-folder-xxx.json [--dry-run]
      known-folder-relocator [--lang en|zh] cleanup --dry-run [--remove-empty-dirs] [--state .state\shell-known-folder-xxx.json]
      known-folder-relocator [--lang en|zh] cleanup --force [--remove-empty-dirs] [--state .state\shell-known-folder-xxx.json]
    """;

    private const string ChineseHelpText = """
    用法:
      known-folder-relocator [--lang en|zh] verify
      known-folder-relocator [--lang en|zh] migrate --target-drive E [--copy-strategy CopyMissing|NoCopy|BackupConflicts] [--dry-run]
      known-folder-relocator [--lang en|zh] migrate --target-root E:\Users\pyo1024 [--dry-run]
      known-folder-relocator [--lang en|zh] attach --target-drive E [--no-copy] [--dry-run]
      known-folder-relocator [--lang en|zh] restore --state .state\shell-known-folder-xxx.json [--dry-run]
      known-folder-relocator [--lang en|zh] cleanup --dry-run [--remove-empty-dirs] [--state .state\shell-known-folder-xxx.json]
      known-folder-relocator [--lang en|zh] cleanup --force [--remove-empty-dirs] [--state .state\shell-known-folder-xxx.json]
    """;
}
