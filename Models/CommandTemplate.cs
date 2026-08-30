namespace FFmpegUI.Models;

/// <summary>命令行模板：{input} / {output} 为占位符，执行时替换为实际路径。</summary>
public sealed class CommandTemplate
{
    public string Name { get; set; } = string.Empty;

    public string Arguments { get; set; } = string.Empty;

    /// <summary>是否为内置模板（内置模板不允许删除）。</summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>模板用途说明。</summary>
    public string Description { get; set; } = string.Empty;
}
