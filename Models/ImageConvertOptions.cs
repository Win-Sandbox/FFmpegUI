using FFmpegUI.Services;
using System;
using System.Text.Json.Serialization;

namespace FFmpegUI.Models;

/// <summary>图片尺寸的处理方式。</summary>
public enum ImageResizeMode
{
    /// <summary>保持原始尺寸。</summary>
    None,

    /// <summary>指定宽度，高度按比例自动计算。</summary>
    ByWidth,

    /// <summary>指定高度，宽度按比例自动计算。</summary>
    ByHeight,

    /// <summary>指定宽高（可能改变宽高比）。</summary>
    Exact,

    /// <summary>限制在指定矩形内（等比缩放，不裁切、不变形）。</summary>
    Fit
}

/// <summary>图片转换参数。
/// 与 <see cref="FfmpegOptions"/> 的区别：图片是单帧位图，
/// 不涉及码率、时长、轨道选择等视频概念，故单独建模。</summary>
public sealed class ImageConvertOptions
{
    /// <summary>目标格式（决定编码器与质量参数）。</summary>
    [JsonIgnore]
    public ImageFormatInfo TargetFormat { get; set; } =
        ImageFormatCatalog.Formats[ImageFormatCatalog.DefaultFormatIndex];

    /// <summary>目标格式的扩展名（序列化用）。
    /// 反序列化时据此从 <see cref="ImageFormatCatalog"/> 找回权威实例，避免孤立对象。</summary>
    [JsonInclude]
    [JsonPropertyName("TargetFormatExtension")]
    public string TargetFormatExtension
    {
        get => TargetFormat?.Extension ?? string.Empty;
        set
        {
            var found = ImageFormatCatalog.FindByExtension(value);
            TargetFormat = found ?? ImageFormatCatalog.Formats[ImageFormatCatalog.DefaultFormatIndex];
        }
    }

    /// <summary>质量值。含义由 <see cref="ImageFormatInfo.QualityMode"/> 决定：
    /// QScale 为 2–31（越小越好），Quality 为 0–100（越大越好）。</summary>
    public int Quality { get; set; } = 80;

    #region 尺寸

    public ImageResizeMode ResizeMode { get; set; } = ImageResizeMode.None;

    /// <summary>目标宽度（像素）。0 表示不适用。</summary>
    public int Width { get; set; }

    /// <summary>目标高度（像素）。0 表示不适用。</summary>
    public int Height { get; set; }

    /// <summary>等比缩放时是否允许放大到超过原始尺寸。
    /// 关闭可避免小图被拉伸失真。</summary>
    public bool AllowUpscale { get; set; }

    #endregion

    #region 画面调整

    /// <summary>旋转角度（0 / 90 / 180 / 270）。</summary>
    public int Rotate { get; set; }

    /// <summary>水平翻转。</summary>
    public bool FlipHorizontal { get; set; }

    /// <summary>垂直翻转。</summary>
    public bool FlipVertical { get; set; }

    /// <summary>灰度化（转为黑白）。</summary>
    public bool Grayscale { get; set; }

    #endregion

    /// <summary>是否仅在源图片尺寸大于目标时才缩放。
    /// 与 <see cref="ImageResizeMode.Fit"/> 配合使用。</summary>
    public bool ShrinkOnly { get; set; }

    /// <summary>拷贝一份参数（并发转换多个文件时避免互相干扰）。</summary>
    public ImageConvertOptions Clone() => (ImageConvertOptions)MemberwiseClone();

    /// <summary>创建默认配置。</summary>
    public static ImageConvertOptions CreateDefault() => new();
}
