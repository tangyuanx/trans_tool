using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TransTool
{
    internal static class Tests
    {
        [STAThread]
        private static int Main()
        {
            CheckCodec();
            CheckUpdates().GetAwaiter().GetResult();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (var window = new MainWindow())
            {
                window.CreateControl();
                var boxes = Descendants(window).OfType<TextBox>().ToArray();
                var input = boxes.Single(x => !x.ReadOnly);
                var output = boxes.Single(x => x.ReadOnly);
                input.Text = "AB";
                Equal("65 66", output.Text);
                input.Text = "中";
                Equal("", output.Text);
            }
            Console.WriteLine("ASCII conversion, main window and update service passed.");
            return 0;
        }

        private static System.Collections.Generic.IEnumerable<Control> Descendants(Control control)
        {
            foreach (Control child in control.Controls)
            {
                yield return child;
                foreach (var nested in Descendants(child)) yield return nested;
            }
        }

        private static void CheckCodec()
        {
            Equal("65 32 66 13 10", AsciiCodec.Encode("A B\r\n", 10, false));
            Equal("41 00 09 7F", AsciiCodec.Encode(@"A\0\t\x7f", 16, true));
            Equal("01000001", AsciiCodec.Encode("A", 2, false));
            Equal("92 110", AsciiCodec.Encode(@"\n", 10, false));
            int count;
            Equal(@"ABCD\r\n\0\x7F\\", AsciiCodec.Decode("65, 0x42；0b1000011\n68 13 10 0 127 92", 10, out count));
            Equal(9, count);
            Equal(@"\x0B", AsciiCodec.Decode("0B", 16, out count));
            Equal("A", AsciiCodec.Decode("01000001", 2, out count));
            Equal("", AsciiCodec.Decode(" , ；", 10, out count));
            Equal(0, count);
            foreach (var radix in new[] { 10, 16, 2 })
            {
                var raw = new string(Enumerable.Range(0, 128).Select(x => (char)x).ToArray());
                var codes = AsciiCodec.Encode(raw, radix, false);
                var escaped = AsciiCodec.Decode(codes, radix, out count);
                Equal(128, count);
                Equal(codes, AsciiCodec.Encode(escaped, radix, true));
            }

            Expect<FormatException>(() => AsciiCodec.Encode("中", 10, false));
            Expect<FormatException>(() => AsciiCodec.Encode("🙂", 10, false));
            Expect<FormatException>(() => AsciiCodec.Encode(@"\x80", 10, true));
            Expect<FormatException>(() => AsciiCodec.Decode("65 128", 10, out count));
            Expect<FormatException>(() => AsciiCodec.Decode("0x", 16, out count));
            Expect<FormatException>(() => AsciiCodec.Decode("02", 2, out count));
        }

        private static async Task CheckUpdates()
        {
            const string version = "v1.10.0";
            const string url = "https://github.com/tangyuanx/trans_tool/releases/download/" + version + "/" + UpdateService.SetupName;
            byte[] bytes = Encoding.UTF8.GetBytes("test installer");
            string hash;
            using (var sha = SHA256.Create())
                hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();

            string metadata = "{\"tag_name\":\"" + version + "\",\"assets\":[{\"name\":\"" + UpdateService.SetupName +
                "\",\"state\":\"uploaded\",\"browser_download_url\":\"" + url + "\",\"size\":" + bytes.Length +
                ",\"digest\":\"sha256:" + hash + "\"}]}";
            var source = new FakeHttp(request =>
            {
                if (request.RequestUri.Host == "api.github.com") return new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = new StringContent(metadata) };
                if (request.RequestUri.Host == "github.com")
                {
                    var redirect = new HttpResponseMessage(HttpStatusCode.Redirect);
                    redirect.Headers.Location = new Uri("https://release-assets.githubusercontent.com/example");
                    return redirect;
                }
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            });

            using (var service = new UpdateService(source))
            {
                Equal(null, await service.CheckAsync(new Version(1, 10, 0, 0), CancellationToken.None));
                var package = await service.CheckAsync(new Version(1, 9, 0, 0), CancellationToken.None);
                Equal(new Version(1, 10, 0), package.Version);
                string path = await service.DownloadAsync(package, null, CancellationToken.None);
                try { Equal(true, UpdateService.HashMatches(path, hash)); }
                finally { UpdateService.DeleteCache(Path.GetDirectoryName(path)); }

                package.Hash = new string('0', 64);
                Expect<InvalidDataException>(() => service.DownloadAsync(package, null, CancellationToken.None).GetAwaiter().GetResult());
                package.Size--;
                Expect<InvalidDataException>(() => service.DownloadAsync(package, null, CancellationToken.None).GetAwaiter().GetResult());
                package.Download = new Uri("http://github.com/example");
                Expect<InvalidDataException>(() => service.DownloadAsync(package, null, CancellationToken.None).GetAwaiter().GetResult());
            }
        }

        private static void Equal(object expected, object actual)
        {
            if (!Equals(expected, actual)) throw new Exception("Expected " + expected + "; got " + actual);
        }

        private static void Expect<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new Exception("Expected " + typeof(T).Name);
        }

        private sealed class FakeHttp : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> send;
            internal FakeHttp(Func<HttpRequestMessage, HttpResponseMessage> send) { this.send = send; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
            {
                return Task.FromResult(send(request));
            }
        }
    }
}
