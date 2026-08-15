using Mermaid2Pptx;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<MermaidPptxConverter>();

var app = builder.Build();

app.MapGet("/", () => Results.Content(IndexHtml(), "text/html; charset=utf-8"));

app.MapPost("/convert", async (HttpRequest request, MermaidPptxConverter converter, CancellationToken cancellationToken) =>
{
    var form = await request.ReadFormAsync(cancellationToken);
    var code = form["code"].ToString();
    var fileName = SanitizeFileName(form["fileName"].ToString());

    if (string.IsNullOrWhiteSpace(code))
    {
        return Results.BadRequest("Mermaid or draw.io source is required.");
    }

    var outputDirectory = Path.Combine(app.Environment.ContentRootPath, "out");
    Directory.CreateDirectory(outputDirectory);
    var outputPath = Path.Combine(outputDirectory, fileName);

    if (DrawIoDocumentParser.LooksLikeDrawIo(code))
    {
        converter.ConvertDrawIoXml(code, outputPath);
    }
    else
    {
        await converter.ConvertMermaidCodeAsync(code, outputPath, cancellationToken: cancellationToken);
    }
    var bytes = await File.ReadAllBytesAsync(outputPath, cancellationToken);
    return Results.File(
        bytes,
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        fileName);
});

app.Run();

static string SanitizeFileName(string fileName)
{
    fileName = string.IsNullOrWhiteSpace(fileName) ? "mermaid-native.pptx" : fileName.Trim();
    if (!fileName.EndsWith(".pptx", StringComparison.OrdinalIgnoreCase))
    {
        fileName += ".pptx";
    }

    foreach (var invalid in Path.GetInvalidFileNameChars())
    {
        fileName = fileName.Replace(invalid, '-');
    }

    return fileName;
}

static string IndexHtml() =>
    """
<!doctype html>
<html lang="zh-Hant">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Mermaid2PPTX</title>
  <style>
    :root {
      color-scheme: light;
      --ink: #172033;
      --muted: #64748b;
      --line: #ccd6e3;
      --panel: #f6f8fb;
      --accent: #2563eb;
      --accent-strong: #1d4ed8;
      --good: #0f766e;
    }

    * { box-sizing: border-box; }

    body {
      margin: 0;
      min-height: 100vh;
      font-family: "Segoe UI", Arial, sans-serif;
      color: var(--ink);
      background: #ffffff;
    }

    main {
      min-height: 100vh;
      display: grid;
      grid-template-rows: auto 1fr;
    }

    header {
      height: 64px;
      border-bottom: 1px solid var(--line);
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 0 28px;
      background: #ffffff;
    }

    h1 {
      margin: 0;
      font-size: 20px;
      font-weight: 650;
      letter-spacing: 0;
    }

    .status {
      color: var(--muted);
      font-size: 13px;
    }

    .workspace {
      display: grid;
      grid-template-columns: minmax(360px, 0.85fr) minmax(420px, 1.15fr);
      min-height: 0;
    }

    form {
      min-height: 0;
      display: grid;
      grid-template-rows: auto 1fr auto;
      border-right: 1px solid var(--line);
      background: var(--panel);
    }

    .bar {
      min-height: 76px;
      padding: 18px 20px;
      display: grid;
      grid-template-columns: 1fr auto;
      gap: 12px;
      align-items: end;
      border-bottom: 1px solid var(--line);
    }

    label {
      display: grid;
      gap: 6px;
      font-size: 13px;
      color: var(--muted);
      font-weight: 600;
    }

    input {
      width: 100%;
      height: 38px;
      border: 1px solid var(--line);
      border-radius: 6px;
      padding: 0 12px;
      font: inherit;
      color: var(--ink);
      background: #ffffff;
    }

    textarea {
      width: 100%;
      height: 100%;
      min-height: 520px;
      resize: none;
      border: 0;
      outline: 0;
      padding: 20px;
      font: 15px/1.55 Consolas, "Cascadia Mono", monospace;
      color: #101827;
      background: #ffffff;
    }

    button {
      height: 38px;
      border: 0;
      border-radius: 6px;
      padding: 0 16px;
      font: inherit;
      font-weight: 650;
      color: #ffffff;
      background: var(--accent);
      cursor: pointer;
    }

    button:hover { background: var(--accent-strong); }
    button:disabled { opacity: 0.55; cursor: wait; }

    .footer {
      min-height: 52px;
      padding: 12px 20px;
      display: flex;
      align-items: center;
      justify-content: space-between;
      border-top: 1px solid var(--line);
      color: var(--muted);
      font-size: 13px;
    }

    .preview {
      min-height: 0;
      padding: 24px;
      display: grid;
      grid-template-rows: auto 1fr;
      gap: 16px;
      background: #ffffff;
    }

    .preview-head {
      display: flex;
      align-items: baseline;
      justify-content: space-between;
      gap: 16px;
    }

    .preview-title {
      font-size: 16px;
      font-weight: 650;
    }

    .stage {
      border: 1px solid var(--line);
      border-radius: 8px;
      min-height: 520px;
      overflow: auto;
      display: grid;
      place-items: center;
      background:
        linear-gradient(#eef2f7 1px, transparent 1px),
        linear-gradient(90deg, #eef2f7 1px, transparent 1px);
      background-size: 24px 24px;
      padding: 32px;
    }

    .mermaid {
      background: #ffffff;
      padding: 24px;
      border-radius: 8px;
      border: 1px solid #e2e8f0;
      max-width: 100%;
    }

    .ok { color: var(--good); }

    @media (max-width: 900px) {
      header { padding: 0 18px; }
      .workspace { grid-template-columns: 1fr; }
      form { border-right: 0; border-bottom: 1px solid var(--line); }
      textarea { min-height: 360px; }
      .stage { min-height: 360px; }
    }
  </style>
</head>
<body>
  <main>
    <header>
      <h1>Mermaid2PPTX</h1>
      <div class="status" id="status">ready</div>
    </header>

    <section class="workspace">
      <form method="post" action="/convert" id="convertForm">
        <div class="bar">
          <label>
            Output
            <input name="fileName" value="mermaid-native.pptx" autocomplete="off">
          </label>
          <button id="submitButton" type="submit">Download PPTX</button>
        </div>
        <textarea name="code" id="code" spellcheck="false">flowchart LR
  A([Start]) --> B{Decision}
  B -- Yes --> C[Native PowerPoint shapes]
  B -- No --> D[Warning report]
  C --> E([Done])
  D --> E</textarea>
        <div class="footer">
          <span>Mermaid SVG or draw.io XML → DrawingML</span>
          <span class="ok">native shapes</span>
        </div>
      </form>

      <section class="preview">
        <div class="preview-head">
          <div class="preview-title">Preview</div>
          <div class="status" id="previewStatus">rendered</div>
        </div>
        <div class="stage">
          <pre class="mermaid" id="preview"></pre>
        </div>
      </section>
    </section>
  </main>

  <script type="module">
    import mermaid from "https://cdn.jsdelivr.net/npm/mermaid@11/dist/mermaid.esm.min.mjs";
    mermaid.initialize({ startOnLoad: false, securityLevel: "loose" });

    const code = document.getElementById("code");
    const preview = document.getElementById("preview");
    const previewStatus = document.getElementById("previewStatus");
    const form = document.getElementById("convertForm");
    const submitButton = document.getElementById("submitButton");
    const status = document.getElementById("status");
    let renderTimer;

    async function renderPreview() {
      const source = code.value;
      if (/<mxfile[\s>]|<mxGraphModel[\s>]/i.test(source)) {
        preview.removeAttribute("data-processed");
        preview.textContent = "draw.io XML detected. Download PPTX to convert native DrawingML shapes.";
        previewStatus.textContent = "draw.io";
        return;
      }

      previewStatus.textContent = "rendering";
      preview.removeAttribute("data-processed");
      preview.textContent = source;
      try {
        await mermaid.run({ nodes: [preview] });
        previewStatus.textContent = "rendered";
      } catch (error) {
        previewStatus.textContent = "syntax error";
      }
    }

    code.addEventListener("input", () => {
      clearTimeout(renderTimer);
      renderTimer = setTimeout(renderPreview, 350);
    });

    form.addEventListener("submit", () => {
      status.textContent = "converting";
      submitButton.disabled = true;
      setTimeout(() => {
        status.textContent = "ready";
        submitButton.disabled = false;
      }, 3500);
    });

    await renderPreview();
  </script>
</body>
</html>
""";
