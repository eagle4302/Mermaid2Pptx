import { mkdir, readFile, writeFile } from "node:fs/promises";
import path from "node:path";
import { pathToFileURL } from "node:url";

const [pptxPathArg, outDirArg] = process.argv.slice(2);
if (!pptxPathArg || !outDirArg) {
  console.error("Usage: node tools/render-pptx-previews.mjs <deck.pptx> <out-dir>");
  process.exit(2);
}

const pptxPath = path.resolve(pptxPathArg);
const outDir = path.resolve(outDirArg);
await mkdir(outDir, { recursive: true });

const nodeModules =
  process.env.CODEX_NODE_MODULES ||
  "C:\\Users\\User\\.cache\\codex-runtimes\\codex-primary-runtime\\dependencies\\node\\node_modules";
const artifactTool = pathToFileURL(
  path.join(nodeModules, "@oai", "artifact-tool", "dist", "artifact_tool.mjs"),
).href;

const { PresentationFile } = await import(artifactTool);
const bytes = await readFile(pptxPath);
const presentation = await PresentationFile.importPptx(bytes);
const exported = [];

for (let index = 0; index < presentation.slides.count; index += 1) {
  const slide = presentation.slides.items[index];
  const base = `slide-${String(index + 1).padStart(2, "0")}`;
  const png = await slide.export({ format: "png" });
  const layout = await slide.export({ format: "layout" });
  const pngPath = path.join(outDir, `${base}.png`);
  const layoutPath = path.join(outDir, `${base}.layout.json`);
  await writeFile(pngPath, new Uint8Array(await png.arrayBuffer()));
  await writeFile(layoutPath, JSON.stringify(layout, null, 2), "utf8");
  exported.push({ slide: index + 1, pngPath, layoutPath });
}

await writeFile(
  path.join(outDir, "manifest.json"),
  JSON.stringify({ pptxPath, slideCount: presentation.slides.count, exported }, null, 2),
  "utf8",
);
console.log(`Rendered ${presentation.slides.count} slides to ${outDir}`);
