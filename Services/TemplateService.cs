using FFmpegUI.Helpers;
using FFmpegUI.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FFmpegUI.Services;

/// <summary>命令行模板的持久化与内置模板目录。
/// 内置模板覆盖 FFmpeg 常见高级用法，用户亦可保存自己的模板。</summary>
public static class TemplateService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>内置模板（覆盖 FFmpeg 常见高级用法）。
    /// 名称与说明均经本地化，未命中资源时使用下方的中文文本。</summary>
    public static IReadOnlyList<CommandTemplate> BuiltInTemplates { get; } = new List<CommandTemplate>
    {
        Tpl("H265Archive", "H.265 高质量归档",
            "用 libx265 以 CRF 20 慢速编码，体积比 H.264 小约 40%",
            "-i \"{input}\" -c:v libx265 -crf 20 -preset slow -c:a aac -b:a 192k \"{output}\""),

        Tpl("ToGif", "转换为 GIF 动图",
            "生成高质量调色板 GIF，适合短片段循环播放",
            "-i \"{input}\" -vf \"fps=12,scale=640:-1:flags=lanczos,split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse\" -loop 0 \"{output}\""),

        Tpl("TextWatermark", "添加文字水印",
            "在左上角绘制文字（drawtext 滤镜）",
            "-i \"{input}\" -vf \"drawtext=text='FFmpegUI':fontsize=24:fontcolor=white:x=10:y=10\" -c:a copy \"{output}\""),

        Tpl("DoubleSpeed", "2 倍速播放",
            "视频 setpts + 音频 atempo 同步加速",
            "-i \"{input}\" -filter_complex \"[0:v]setpts=0.5*PTS[v];[0:a]atempo=2.0[a]\" -map \"[v]\" -map \"[a]\" \"{output}\""),

        Tpl("Reverse", "视频倒放",
            "音视频同时倒放（需完整解码到内存，适合短视频）",
            "-i \"{input}\" -vf reverse -af areverse \"{output}\""),

        Tpl("Denoise", "音频降噪",
            "afftdn 自适应降噪，视频流直接复制",
            "-i \"{input}\" -af \"afftdn=nf=-30\" -c:v copy \"{output}\""),

        Tpl("Loudnorm", "音频响度标准化",
            "按 EBU R128 标准把响度归一到 -16 LUFS",
            "-i \"{input}\" -af \"loudnorm=I=-16:TP=-1.5:LRA=11\" -c:v copy \"{output}\""),

        Tpl("ExtractMp3", "提取音频为 MP3",
            "去掉视频，音频转 MP3 320 kbps",
            "-i \"{input}\" -vn -c:a libmp3lame -b:a 320k \"{output}\""),

        Tpl("Delogo", "去除水印（delogo）",
            "用 delogo 插值覆盖指定矩形区域，需自行修改坐标",
            "-i \"{input}\" -vf \"delogo=x=10:y=10:w=100:h=40\" -c:a copy \"{output}\""),

        Tpl("CropBorders", "裁剪黑边",
            "四周各裁掉 10 像素，可按实际黑边修改数值",
            "-i \"{input}\" -vf \"crop=iw-20:ih-20\" -c:a copy \"{output}\""),

        Tpl("ImageToVideo", "图片合成 10 秒视频",
            "单张图片循环合成为 10 秒 H.264 视频",
            "-loop 1 -i \"{input}\" -c:v libx264 -t 10 -pix_fmt yuv420p -vf \"scale=trunc(iw/2)*2:trunc(ih/2)*2\" \"{output}\""),

        Tpl("VerticalBlur", "视频转竖屏（模糊填充）",
            "9:16 竖屏，两侧用模糊画面填充",
            "-i \"{input}\" -filter_complex \"[0:v]split[a][b];[a]scale=1080:1920:force_original_aspect_ratio=increase,crop=1080:1920,gblur=sigma=25[bg];[b]scale=1080:1920:force_original_aspect_ratio=decrease[fg];[bg][fg]overlay=(W-w)/2:(H-h)/2\" -c:a copy \"{output}\""),

        Tpl("Nvenc", "H.264 硬件编码（NVENC）",
            "使用 NVIDIA 显卡硬件编码，速度远快于软件编码",
            "-hwaccel cuda -i \"{input}\" -c:v h264_nvenc -preset p4 -cq 22 -c:a aac \"{output}\""),

        Tpl("SpriteSheet", "生成雪碧图",
            "每 10 秒取一帧，拼成 5×5 缩略图预览图",
            "-i \"{input}\" -vf \"fps=1/10,scale=160:90,tile=5x5\" -frames:v 1 \"{output}\""),

        Tpl("Remux", "仅复制流（快速换容器）",
            "不重新编码，仅更换封装格式",
            "-i \"{input}\" -c copy \"{output}\""),

        // —— 以下补充此前未覆盖的常用场景 ——

        Tpl("BurnSubtitle", "烧录字幕到画面",
            "把字幕永久压进画面（subtitles 滤镜），播放器无需外挂字幕；需与视频同目录的同名字幕文件",
            "-i \"{input}\" -vf \"subtitles='{input}'\" -c:a copy \"{output}\""),

        Tpl("Deinterlace", "去隔行扫描",
            "yadif 把隔行扫描老片转为逐行，消除横向拉丝",
            "-i \"{input}\" -vf \"yadif=1:-1:0\" -c:a copy \"{output}\""),

        Tpl("DeinterlaceBwdif", "去隔行扫描（bwdif 高质量）",
            "bwdif 画质优于 yadif，适合 DVD/电视采集源",
            "-i \"{input}\" -vf \"bwdif=mode=send_field:parity=auto:deint=all\" -c:a copy \"{output}\""),

        // vidstab 防抖必须两遍：第一遍只分析并落盘 transforms.trf（输出到空设备），
        // 第二遍读取该文件做变换。注意不能用 shell 的 && 串联——参数以 ArgumentList
        // 直接传给 ffmpeg，不经过 shell，&& 会被当作 ffmpeg 参数导致失败。
        Tpl("StabilizePass1", "画面防抖（第 1 遍：分析抖动）",
            "仅分析并生成 transforms.trf，不输出视频；随后请执行第 2 遍",
            "-i \"{input}\" -vf \"vidstabdetect=shakiness=10:accuracy=15:result=transforms.trf\" -f null NUL"),

        Tpl("StabilizePass2", "画面防抖（第 2 遍：稳定画面）",
            "读取第 1 遍生成的 transforms.trf 输出防抖后的视频",
            "-i \"{input}\" -vf \"vidstabtransform=input=transforms.trf:smoothing=30\" -c:a copy \"{output}\""),

        Tpl("Deshake", "画面防抖（单遍 deshake）",
            "单遍防抖，速度较 vidstab 快，适合轻微抖动",
            "-i \"{input}\" -vf \"deshake=rx=16:ry=16\" -c:a copy \"{output}\""),

        Tpl("DenoiseVideo", "视频降噪（hqdn3d）",
            "hqdn3d 时空域降噪，适合噪点较多的实拍素材",
            "-i \"{input}\" -vf \"hqdn3d=4:3:6:4.5\" -c:a copy \"{output}\""),

        Tpl("Sharpen", "画面锐化（unsharp）",
            "unsharp 掩膜锐化，让画面更清晰",
            "-i \"{input}\" -vf \"unsharp=5:5:1.0:5:5:0.0\" -c:a copy \"{output}\""),

        Tpl("FadeInOut", "首尾淡入淡出",
            "开头 1 秒淡入、结尾 1 秒淡出（结尾时间需按实际时长修改）",
            "-i \"{input}\" -vf \"fade=t=in:st=0:d=1,fade=t=out:st=9:d=1\" -c:a copy \"{output}\""),

        Tpl("HdrToSdr", "HDR 转 SDR（tone mapping）",
            "把 HDR10（PQ）内容色调映射为普通 SDR，避免画面发灰",
            "-i \"{input}\" -vf \"zscale=t=linear:npl=100,format=gbrpf32le,zscale=p=bt709:t=bt709:m=bt709:r=tv,tonemap=tonemap=hable:desat=0,zscale=t=bt709:m=bt709:r=tv,format=yuv420p\" -c:a copy \"{output}\""),

        Tpl("SdrToHdr", "SDR 转 HDR10",
            "把 SDR 内容转为 HDR10（PQ 曲线 + BT.2020 色域），需手动确认显示设备支持",
            "-i \"{input}\" -vf \"zscale=t=linear:npl=100,format=gbrpf32le,zscale=p=bt2020:t=smpte2084:m=bt2020nc:r=tv,format=yuv420p10le\" -c:a copy -color_primaries bt2020 -color_trc smpte2084 -colorspace bt2020nc \"{output}\""),

        Tpl("FixTimestamps", "修复时间戳/音画不同步",
            "重建时间戳并强制恒定帧率，修复转码后音画不同步或时长显示异常",
            "-i \"{input}\" -fflags +genpts -vf \"fps=30\" -fps_mode cfr -af \"aresample=async=1:first_pts=0\" \"{output}\""),

        Tpl("AudioSync", "音画同步（音频延迟 0.5 秒）",
            "-itsoffset 正值延后音频、负值提前；此处音频延后 0.5 秒",
            "-i \"{input}\" -itsoffset 0.5 -i \"{input}\" -map 0:v -map 1:a -c copy \"{output}\""),

        Tpl("MuteAudio", "去除音轨（静音）",
            "保留视频流，完全移除音频",
            "-i \"{input}\" -an -c:v copy \"{output}\""),

        Tpl("ReplaceAudio", "替换音轨",
            "用第二个输入替换音频；需自行把 {input} 改为音频文件路径后添加到附加输入",
            "-i \"{input}\" -i \"{input}\" -map 0:v -map 1:a -c:v copy -shortest \"{output}\""),

        Tpl("MixAudio", "混音（两路音频合并）",
            "amix 把两路音频混合为一路，normalize=0 防止音量减半",
            "-i \"{input}\" -i \"{input}\" -filter_complex \"[0:a][1:a]amix=inputs=2:duration=first:dropout_transition=2:normalize=0[a]\" -map 0:v -map \"[a]\" -c:v copy -shortest \"{output}\""),

        Tpl("PictureInPicture", "画中画（右下角小窗）",
            "把第二个输入缩放后叠到主画面右下角；需自行指定第二个输入",
            "-i \"{input}\" -i \"{input}\" -filter_complex \"[1:v]scale=iw/4:ih/4[pip];[0:v][pip]overlay=W-w-10:H-h-10\" -c:a copy \"{output}\""),

        Tpl("AddImageWatermark", "添加图片水印",
            "把 PNG 图片作为水印叠加到右上角；需自行指定水印图片路径",
            "-i \"{input}\" -i \"{input}\" -filter_complex \"[1:v]scale=iw*0.2:-1[wm];[0:v][wm]overlay=W-w-10:10\" -c:a copy \"{output}\""),

        Tpl("Rotate90", "旋转 90 度",
            "顺时针旋转 90°（transpose=1），逆时针用 transpose=2",
            "-i \"{input}\" -vf \"transpose=1\" -c:a copy \"{output}\""),

        Tpl("PadSquare", "填充为正方形（1:1）",
            "用黑色填充两侧，输出 1:1 方形视频，适合社交媒体",
            "-i \"{input}\" -vf \"scale=1080:1080:force_original_aspect_ratio=decrease,pad=1080:1080:(ow-iw)/2:(oh-ih)/2:black\" -c:a copy \"{output}\""),

        Tpl("ConcatSameCodec", "拼接同格式视频（无损快速）",
            "使用 concat 分离器无损拼接编码参数完全一致的视频；需先准备 filelist.txt",
            "-f concat -safe 0 -i filelist.txt -c copy \"{output}\""),

        Tpl("HlsStream", "生成 HLS 切片",
            "输出 m3u8 索引与 ts 切片，用于网页点播",
            "-i \"{input}\" -c:v libx264 -c:a aac -hls_time 6 -hls_list_size 0 -f hls \"{output}\""),

        Tpl("DashStream", "生成 DASH 切片",
            "输出 mpd 索引与分片，用于自适应码率流媒体",
            "-i \"{input}\" -c:v libx264 -c:a aac -f dash \"{output}\""),

        Tpl("RecordDesktop", "录制屏幕（gdigrab）",
            "Windows 下用 gdigrab 全屏录制；Ctrl+C 或设置时长结束",
            "-f gdigrab -framerate 30 -i desktop -c:v libx264 -pix_fmt yuv420p \"{output}\""),

        Tpl("ExtractFrames", "提取全部帧为图片",
            "把视频每一帧导出为 PNG 序列，文件名 0001.png、0002.png…",
            "-i \"{input}\" -f image2 \"{output}\""),

        Tpl("FramesToVideo", "图片序列合成视频",
            "把 img%04d.png 序列合成为 30fps 视频；输出文件名需为 .mp4",
            "-framerate 30 -f image2 -i img%04d.png -c:v libx264 -pix_fmt yuv420p \"{output}\""),

        Tpl("SlowMotion", "慢动作 0.5 倍",
            "视频放慢一倍并保持音调（atempo 0.5 与 setpts 2 配对）",
            "-i \"{input}\" -filter_complex \"[0:v]setpts=2.0*PTS[v];[0:a]atempo=0.5[a]\" -map \"[v]\" -map \"[a]\" \"{output}\""),

        Tpl("Speed125", "1.25 倍速（保留音调）",
            "轻微加速，音频用 atempo 保持音调不变",
            "-i \"{input}\" -filter_complex \"[0:v]setpts=0.8*PTS[v];[0:a]atempo=1.25[a]\" -map \"[v]\" -map \"[a]\" \"{output}\""),

        Tpl("SilenceRemove", "去除静音片段",
            "自动剪掉音轨中的静音部分，适合录音/会议录像",
            "-i \"{input}\" -af \"silenceremove=start_periods=1:start_threshold=-50dB:detection=peak\" -c:v copy \"{output}\""),

        Tpl("NormalizeVolume", "音量动态归一化",
            "dynaudnorm 动态归一化，整体音量更均衡（不同于 loudnorm 的静态归一）",
            "-i \"{input}\" -af \"dynaudnorm=f=200:g=15\" -c:v copy \"{output}\""),

        Tpl("MonoToStereo", "单声道转立体声",
            "用 pan 滤镜复制声道，输出双声道",
            "-i \"{input}\" -af \"pan=stereo|c0=c0|c1=c0\" -c:v copy \"{output}\""),

        Tpl("BoostBass", "增强低音",
            "用 equalizer 提升 100Hz 以下低频",
            "-i \"{input}\" -af \"equalizer=f=100:t=q:w=2:g=8\" -c:v copy \"{output}\""),

        Tpl("CompressDynamic", "压缩动态范围",
            "acompressor 压缩动态范围，让小声更清晰、大声不刺耳",
            "-i \"{input}\" -af \"acompressor=threshold=0.089:ratio=9:attack=200:release=1000\" -c:v copy \"{output}\""),

        Tpl("Grayscale", "转为黑白",
            "去色为灰度视频，视频流需重新编码",
            "-i \"{input}\" -vf \"hue=s=0\" -c:a copy \"{output}\""),

        Tpl("BlurBackground", "背景模糊（填充竖屏）",
            "保留主体清晰、背景高斯模糊，适合竖屏短视频",
            "-i \"{input}\" -filter_complex \"[0:v]scale=1080:1920:force_original_aspect_ratio=increase,crop=1080:1920,gblur=sigma=20[bg];[0:v]scale=1080:1920:force_original_aspect_ratio=decrease[fg];[bg][fg]overlay=(W-w)/2:(H-h)/2\" -c:a copy \"{output}\""),

        // 两遍编码同理：两遍需分别执行，不能用 && 串联
        Tpl("TwoPass1", "H.264 两遍编码（第 1 遍：分析）",
            "生成码率统计文件，不输出视频；随后请执行第 2 遍",
            "-i \"{input}\" -c:v libx264 -b:v 2000k -pass 1 -f null NUL"),

        Tpl("TwoPass2", "H.264 两遍编码（第 2 遍：编码）",
            "读取第 1 遍的统计信息完成编码，码率控制更精确",
            "-i \"{input}\" -c:v libx264 -b:v 2000k -pass 2 -c:a aac -b:a 192k \"{output}\""),

        Tpl("MetadataStrip", "清除全部元数据",
            "去掉标题、作者、拍摄参数等信息，便于分享隐私",
            "-i \"{input}\" -map_metadata -1 -map_chapters -1 -c copy \"{output}\""),

        Tpl("SetMetadata", "写入标题与作者",
            "写入自定义元数据；可按需修改 title / artist 的值",
            "-i \"{input}\" -c copy -metadata title=\"我的视频\" -metadata artist=\"FFmpegUI\" \"{output}\""),

        Tpl("FixRotation", "修正手机旋转角度",
            "按视频的旋转元数据把画面转正并清除旋转标记",
            "-i \"{input}\" -vf \"transpose=1\" -metadata:s:v:0 rotate=0 -c:a copy \"{output}\"")
    };

    /// <summary>构造一个内置模板，名称与说明按 Tpl_&lt;key&gt;_Name / _Desc 键本地化。</summary>
    private static CommandTemplate Tpl(string key, string name, string description, string arguments)
        => new()
        {
            Name = StringResources.GetOr($"Tpl_{key}_Name", name),
            Description = StringResources.GetOr($"Tpl_{key}_Desc", description),
            Arguments = arguments,
            IsBuiltIn = true
        };

    /// <summary>模板文件路径。</summary>
    public static string TemplatePath => Path.Combine(App.AppDataPath, "templates.json");

    /// <summary>加载用户自定义模板；文件不存在时返回空列表。</summary>
    public static List<CommandTemplate> LoadCustom()
    {
        try
        {
            if (File.Exists(TemplatePath))
            {
                var json = File.ReadAllText(TemplatePath);
                return JsonSerializer.Deserialize<List<CommandTemplate>>(json, SerializerOptions) ?? new();
            }
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "TemplateService.LoadCustom");
        }

        return new List<CommandTemplate>();
    }

    /// <summary>保存用户自定义模板。</summary>
    public static void SaveCustom(IEnumerable<CommandTemplate> templates)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(TemplatePath)!);
            var list = templates.Where(t => !t.IsBuiltIn).ToList();
            File.WriteAllText(TemplatePath, JsonSerializer.Serialize(list, SerializerOptions));
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "TemplateService.SaveCustom");
        }
    }
}
