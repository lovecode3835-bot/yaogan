// FightstickLab 便携版启动器
// 两种工作模式：
//   1. 目录模式：同目录存在 FightstickLab.Core.exe（真身），运行库就在同目录，
//      只需设置 DOTNET_ROOT 后启动它。
//   2. 单文件模式：自身末尾附带 zip 数据（FSLZIP01 + 长度），首次运行解压到
//      %LOCALAPPDATA%\FightstickLabPortable\app，之后版本一致则直接复用。
// 要求：目标机器安装 .NET Framework 4.5+（Win7 SP1 及以上系统默认都有）。
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace FightstickLabPortable
{
    internal static class Program
    {
        private const string Magic = "FSLZIP01";
        private const string AppExe = "FightstickLab.exe";
        private const string CoreExe = "FightstickLab.Core.exe";

        [STAThread]
        private static int Main()
        {
            try
            {
                Log("launcher start");
                string exePath = Assembly.GetExecutingAssembly().Location;
                Log("exe path: " + exePath);
                string exeDir = Path.GetDirectoryName(exePath);
                string appDir;
                bool folderMode = File.Exists(Path.Combine(exeDir, CoreExe));
                Log("folder mode: " + folderMode.ToString());

                if (folderMode)
                {
                    // 目录模式：真身与运行库都在本目录
                    appDir = exeDir;
                }
                else
                {
                    // 单文件模式：从自身解压出应用目录
                    appDir = EnsureExtracted(exePath);
                    if (appDir == null) return 1;
                }

                string targetExe = Path.Combine(appDir, folderMode ? CoreExe : AppExe);
                Log("target: " + targetExe);
                if (!File.Exists(targetExe))
                {
                    Fail("未找到程序文件：" + targetExe);
                    return 1;
                }

                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = targetExe;
                psi.WorkingDirectory = appDir;
                psi.UseShellExecute = false;
                // 关键：让 .NET 5 宿主从应用目录读取自带的运行库
                psi.EnvironmentVariables["DOTNET_ROOT"] = appDir;
                Process.Start(psi);
                Log("launched, exiting");
                return 0;
            }
            catch (Exception ex)
            {
                Log("unhandled: " + ex.ToString());
                Fail("启动失败：" + ex.Message);
                return 1;
            }
        }

        private static void Log(string message)
        {
            try
            {
                string path = Environment.GetEnvironmentVariable("FSL_DEBUG_LOG");
                if (string.IsNullOrEmpty(path)) return;
                File.AppendAllText(path, DateTime.Now.ToString("HH:mm:ss.fff") + " " + message + Environment.NewLine);
            }
            catch { }
        }

        private static string EnsureExtracted(string exePath)
        {
            long fileLen = new FileInfo(exePath).Length;
            Log("file length: " + fileLen.ToString());
            if (fileLen < 16) { Fail("数据不完整。"); return null; }

            byte[] tail = new byte[16];
            using (FileStream fs = File.OpenRead(exePath))
            {
                fs.Seek(-16, SeekOrigin.End);
                if (fs.Read(tail, 0, 16) != 16) { Fail("读取自身失败。"); return null; }
            }
            string magic = Encoding.ASCII.GetString(tail, 0, 8);
            Log("magic: " + magic);
            if (magic != Magic) { Fail("这不是有效的便携版程序。"); return null; }
            long zipLen = BitConverter.ToInt64(tail, 8);
            Log("zip length: " + zipLen.ToString());
            if (zipLen <= 0 || zipLen + 16 > fileLen) { Fail("数据损坏。"); return null; }

            string root = Environment.GetEnvironmentVariable("FSL_PORTABLE_DIR");
            if (string.IsNullOrEmpty(root))
            {
                root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "FightstickLabPortable");
            }
            Log("root: " + root);
            string appDir = Path.Combine(root, "app");
            string markerFile = Path.Combine(root, "version.txt");
            string marker = new FileInfo(exePath).LastWriteTimeUtc.Ticks.ToString() + "-" + zipLen.ToString();

            if (File.Exists(markerFile)
                && File.ReadAllText(markerFile) == marker
                && File.Exists(Path.Combine(appDir, AppExe)))
            {
                Log("reusing existing extraction");
                return appDir; // 已解压过且版本一致，直接复用
            }

            try { Directory.CreateDirectory(root); Log("root dir created"); }
            catch (Exception ex) { Log("create root failed: " + ex.Message); Fail("无法创建缓存目录：" + ex.Message); return null; }

            // 从自身尾部切出 zip
            string tmpZip = Path.Combine(root, "payload.zip");
            try
            {
                using (FileStream fs = File.OpenRead(exePath))
                using (FileStream outp = File.Create(tmpZip))
                {
                    fs.Seek(-(16 + zipLen), SeekOrigin.End);
                    byte[] buf = new byte[1 << 20];
                    long remaining = zipLen;
                    while (remaining > 0)
                    {
                        int n = fs.Read(buf, 0, (int)Math.Min((long)buf.Length, remaining));
                        if (n <= 0) break;
                        outp.Write(buf, 0, n);
                        remaining -= n;
                    }
                }
                Log("zip extracted to temp: " + tmpZip);
            }
            catch (Exception ex) { Log("zip write failed: " + ex.Message); Fail("解压失败：" + ex.Message); return null; }

            string staging = appDir + ".new";
            try
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
                ZipFile.ExtractToDirectory(tmpZip, staging);
                Log("extracted to staging: " + staging);
            }
            catch (Exception ex) { Log("unzip failed: " + ex.Message); Fail("解压失败：" + ex.Message); return null; }

            try { File.Delete(tmpZip); } catch { }

            try { if (Directory.Exists(appDir)) Directory.Delete(appDir, true); } catch { }
            try
            {
                Directory.Move(staging, appDir);
                Log("moved to app dir");
            }
            catch (Exception ex)
            {
                Log("move failed, using staging: " + ex.Message);
                // 旧目录被占用（例如上一次实例还没退出），就用暂存目录启动
                appDir = staging;
            }

            try { File.WriteAllText(markerFile, marker); Log("marker written"); } catch { }
            return appDir;
        }

        private static void Fail(string message)
        {
            MessageBox.Show(message, "FightstickLab 便携版", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
