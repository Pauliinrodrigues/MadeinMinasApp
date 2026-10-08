using System.Drawing.Printing;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

var arguments = args.ToList();
string Option(string name, string fallback) => arguments.IndexOf(name) is var i && i >= 0 && i + 1 < arguments.Count ? arguments[i + 1] : fallback;
var store = Path.GetFullPath(Option("--store", Path.Combine(AppContext.BaseDirectory, "station")));
Directory.CreateDirectory(store);
var configurationPath = Path.Combine(store, "configuration.json");
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
try
{
    if (arguments.Contains("--install"))
    {
        var downloaded = JsonSerializer.Deserialize<Download>(File.ReadAllText(Option("--install", "")), options)!;
        var uri = new Uri(downloaded.ApiBaseUrl);
        if (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback))
            throw new InvalidOperationException("Use HTTPS para uma API remota.");
        if (downloaded.Token.Length != 64)
            throw new InvalidOperationException("Credencial inválida.");
        Atomic(configurationPath, JsonSerializer.Serialize(new Configuration(uri.AbsoluteUri.TrimEnd('/'),
            downloaded.PrinterName, downloaded.PaperName, Convert.ToBase64String(DataProtection.Protect(Encoding.UTF8.GetBytes(downloaded.Token)))), options));
        Console.WriteLine("Estação instalada. Credencial protegida para este usuário do Windows.");
        return 0;
    }
    if (arguments.Contains("--verify"))
    {
        ReceiptPrinter.Verify(Option("--verify", Path.Combine(store, "layout.png")));
        Console.WriteLine("Layout validado: início no topo, acentos, quebra de texto e paginação.");
        return 0;
    }
    var config = JsonSerializer.Deserialize<Configuration>(File.ReadAllText(configurationPath), options)!;
    var printer = ReceiptPrinter.Create(config.PrinterName, config.PaperName);
    if (arguments.Contains("--test-print"))
    {
        ReceiptPrinter.Print(printer, ["MADE IN MINAS", "TESTE DE IMPRESSÃO AUTOMÁTICA", "80 mm / bobina 500 mm", "Sem pedido e sem valor comercial", "FIM DO TESTE"]);
        Console.WriteLine("Teste enviado ao spooler do Windows. Confira o papel físico.");
        return 0;
    }
    using var mutex = new Mutex(false, "Local\\MadeInMinas.PrintAgent");
    try
    { if (!mutex.WaitOne(0)) throw new InvalidOperationException("O agente já está em execução neste Windows."); }
    catch (AbandonedMutexException) { }
    using var http = new HttpClient { BaseAddress = new Uri(config.ApiBaseUrl + "/"), Timeout = TimeSpan.FromSeconds(20) };
    http.DefaultRequestHeaders.Add("X-Print-Key", Encoding.UTF8.GetString(DataProtection.Unprotect(Convert.FromBase64String(config.ProtectedToken))));
    using var stop = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
    var journalPath = Path.Combine(store, "pending.json");
    Console.WriteLine("Agente ativo. Impressora: " + config.PrinterName);
    while (!stop.IsCancellationRequested)
    {
        try
        {
            if (File.Exists(journalPath))
            {
                var pending = JsonSerializer.Deserialize<Pending>(File.ReadAllText(journalPath), options)!;
                // Um reinício entre Print() e a gravação de Submitted resulta em revisão, nunca em reimpressão.
                using var finish = await http.PostAsJsonAsync($"printing/agent/jobs/{pending.Id}/finish",
                    new { pending.ClaimId, pending.Submitted }, stop.Token);
                if (finish.StatusCode == HttpStatusCode.Unauthorized)
                    throw new InvalidOperationException("Credencial revogada. Cadastre a estação novamente.");
                finish.EnsureSuccessStatusCode();
                File.Delete(journalPath);
            }
            using var response = await http.PostAsync("printing/agent/claim", null, stop.Token);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new InvalidOperationException("Credencial revogada. Cadastre a estação novamente.");
            response.EnsureSuccessStatusCode();
            if (response.StatusCode != HttpStatusCode.NoContent)
            {
                var job = (await response.Content.ReadFromJsonAsync<Claim>(options, stop.Token))!;
                Atomic(journalPath, JsonSerializer.Serialize(new Pending(job.Id, job.ClaimId, false), options));
                try
                {
                    ReceiptPrinter.Print(printer, job.Lines);
                    Atomic(journalPath, JsonSerializer.Serialize(new Pending(job.Id, job.ClaimId, true), options));
                    Console.WriteLine($"{DateTimeOffset.Now:O} Envio {job.Id} entregue ao spooler.");
                }
                catch (Exception)
                {
                    Console.WriteLine($"{DateTimeOffset.Now:O} Envio {job.Id} exige conferência da impressora.");
                }
                continue;
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
        catch (InvalidOperationException) { throw; }
        catch (Exception)
        {
            Console.WriteLine($"{DateTimeOffset.Now:O} Sem comunicação com a API. Tentando novamente em 5 segundos.");
        }
        try
        { await Task.Delay(5000, stop.Token); }
        catch (OperationCanceledException) { break; }
    }
    return 0;
}
catch (Exception failure)
{
    // Nunca exibir payloads HTTP, senhas ou a credencial da estação.
    Console.Error.WriteLine(failure is InvalidOperationException ? failure.Message : "Não foi possível iniciar o agente. Confira a configuração e a impressora instalada.");
    return 1;
}

static void Atomic(string path, string content)
{
    var temporary = path + ".tmp";
    using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        file.Write(bytes);
        file.Flush(true);
    }
    File.Move(temporary, path, true);
}

record Download(string ApiBaseUrl, string Token, string PrinterName, string PaperName);
record Configuration(string ApiBaseUrl, string PrinterName, string PaperName, string ProtectedToken);
record Claim(Guid Id, Guid ClaimId, string[] Lines);
record Pending(Guid Id, Guid ClaimId, bool Submitted);

static class ReceiptPrinter
{
    public static PrinterSettings Create(string name, string paperName)
    {
        var printer = new PrinterSettings { PrinterName = name };
        if (!printer.IsValid)
            throw new InvalidOperationException("Impressora não encontrada: " + name);
        var paper = printer.PaperSizes.Cast<PaperSize>().SingleOrDefault(p => p.PaperName == paperName)
            ?? throw new InvalidOperationException("Formato de bobina não encontrado: " + paperName);
        if (paper.Height > 2200)
            throw new InvalidOperationException("Selecione a bobina de 500 mm, em vez de 3000 mm.");
        printer.DefaultPageSettings.PaperSize = paper;
        printer.DefaultPageSettings.Landscape = false;
        printer.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
        var roll = printer.PaperSources.Cast<PaperSource>().FirstOrDefault(source => source.SourceName.Contains("Bobina", StringComparison.OrdinalIgnoreCase));
        if (roll is not null)
            printer.DefaultPageSettings.PaperSource = roll;
        return printer;
    }

    public static void Print(PrinterSettings printer, string[] paragraphs)
    {
        using var document = new PrintDocument
        {
            PrinterSettings = printer,
            DefaultPageSettings = printer.DefaultPageSettings,
            PrintController = new StandardPrintController(),
            DocumentName = "Made in Minas — comanda"
        };
        using var font = new Font("Arial", 10, FontStyle.Regular, GraphicsUnit.Point);
        string[]? lines = null;
        var next = 0;
        document.PrintPage += (_, e) =>
        {
            var graphics = e.Graphics!;
            // Coordenadas físicas em polegadas; origem no canto imprimível do driver.
            graphics.PageUnit = GraphicsUnit.Inch;
            var width = Math.Min(72f / 25.4f, e.PageSettings.PrintableArea.Width / 100);
            lines ??= Wrap(graphics, font, paragraphs, width);
            var bottom = Math.Min(e.PageBounds.Height, e.PageSettings.PrintableArea.Height) / 100 - .04f;
            next = DrawPage(graphics, font, lines, next, bottom);
            e.HasMorePages = next < lines.Length;
        };
        document.Print();
    }

    private static StringFormat Format() => new(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap };
    private static int DrawPage(Graphics graphics, Font font, string[] lines, int next, float bottom)
    {
        var start = next;
        var height = font.GetHeight(graphics.DpiY) / graphics.DpiY + .02f;
        var y = .02f;
        using var format = Format();
        while (next < lines.Length && y + height <= bottom)
        {
            graphics.DrawString(lines[next++], font, Brushes.Black, new PointF(0, y), format);
            y += height;
        }
        if (next == start)
            throw new InvalidOperationException("A área imprimível não comporta o texto.");
        return next;
    }
    private static string[] Wrap(Graphics graphics, Font font, string[] paragraphs, float width)
    {
        var result = new List<string>();
        using var format = Format();
        foreach (var paragraph in paragraphs)
            foreach (var original in paragraph.Replace("\r", "").Split('\n'))
            {
                var remaining = new string(original.Select(c => char.IsControl(c) ? ' ' : c).ToArray());
                if (remaining.Length == 0)
                    result.Add("");
                while (remaining.Length > 0)
                {
                    var low = 1;
                    var high = remaining.Length;
                    while (low < high)
                    {
                        var middle = (low + high + 1) / 2;
                        if (graphics.MeasureString(remaining[..middle], font, int.MaxValue, format).Width <= width)
                            low = middle;
                        else
                            high = middle - 1;
                    }
                    var cut = low;
                    if (cut < remaining.Length && remaining.LastIndexOf(' ', cut - 1, cut) is var space && space > 0)
                        cut = space;
                    result.Add(remaining[..cut]);
                    remaining = remaining[cut..].TrimStart();
                }
            }
        return result.ToArray();
    }

    public static void Verify(string path)
    {
        using var bitmap = new Bitmap(576, 1000);
        bitmap.SetResolution(203, 203);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.PageUnit = GraphicsUnit.Inch;
        graphics.Clear(Color.White);
        using var font = new Font("Arial", 10, FontStyle.Regular, GraphicsUnit.Point);
        var width = 72f / 25.4f;
        var input = new[] { "MADE IN MINAS", "Pedido #123 — PRODUÇÃO", "2 x Hambúrguer", "Obs.: sem cebola, embalar separado", new string('W', 150), "FIM" };
        var lines = Wrap(graphics, font, input, width);
        using var format = Format();
        if (lines.Any(line => graphics.MeasureString(line, font, int.MaxValue, format).Width > width + 0.1f) || lines[^1] != "FIM")
            throw new InvalidOperationException("Falha de quebra de texto.");
        if (DrawPage(graphics, font, lines, 0, 500f / 25.4f - .04f) != lines.Length)
            throw new InvalidOperationException("Uma comanda curta criou páginas adicionais.");
        bitmap.Save(Path.GetFullPath(path), System.Drawing.Imaging.ImageFormat.Png);
        var longLines = Enumerable.Range(0, 350).Select(i => $"Item {i}: pão, carne, queijo — sem cebola").ToArray();
        var next = 0;
        var pages = 0;
        while (next < longLines.Length)
        {
            next = DrawPage(graphics, font, longLines, next, 500f / 25.4f - .04f);
            pages++;
        }
        if (pages < 2 || next != longLines.Length)
            throw new InvalidOperationException("Falha de paginação da comanda longa.");
    }
}

static class DataProtection
{
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Size; public IntPtr Data; }
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CryptProtectData(ref Blob input, string description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    public static byte[] Protect(byte[] value) => Transform(value, true);
    public static byte[] Unprotect(byte[] value) => Transform(value, false);
    private static byte[] Transform(byte[] value, bool protect)
    {
        var input = new Blob { Size = value.Length, Data = Marshal.AllocHGlobal(value.Length) };
        try
        {
            Marshal.Copy(value, 0, input.Data, value.Length);
            var success = protect ? CryptProtectData(ref input, "MadeInMinas.PrintAgent", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out var output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!success)
                throw new InvalidOperationException("Credencial indisponível para este usuário do Windows.");
            try
            { var bytes = new byte[output.Size]; Marshal.Copy(output.Data, bytes, 0, bytes.Length); return bytes; }
            finally { LocalFree(output.Data); }
        }
        finally { Marshal.FreeHGlobal(input.Data); }
    }
}
