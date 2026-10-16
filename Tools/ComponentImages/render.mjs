// Renders the component SVGs to transparent PNGs at twice their size, with NodePen's fonts.
//
//     node render.mjs svg_dir png_dir
//
// The fonts come from Google Fonts, so this needs the network. Each message tab is sized here,
// once the fonts are in, because only the browser knows how wide its text is.
import { chromium } from 'playwright';
import fs from 'fs';
import path from 'path';

const [svgDir, outDir] = process.argv.slice(2);
fs.mkdirSync(outDir, { recursive: true });
const files = fs.readdirSync(svgDir).filter((f) => f.endsWith('.svg')).sort();

const browser = await chromium.launch();
const page = await browser.newPage({ deviceScaleFactor: 2 });
const items = files.map((f) => `<div class="item" data-name="${path.basename(f, '.svg')}">${fs.readFileSync(path.join(svgDir, f), 'utf8')}</div>`).join('\n');
await page.setContent(`<!doctype html><html><head>
<link href="https://fonts.googleapis.com/css2?family=Overpass:wght@700;800&family=Overpass+Mono:wght@600&display=block" rel="stylesheet">
<style>html,body{margin:0;background:transparent} .item{display:inline-block;margin:4px} svg{display:block}</style>
</head><body>${items}</body></html>`, { waitUntil: 'networkidle' });
await page.evaluate(() => document.fonts.ready);

const loaded = await page.evaluate(() => [...document.fonts].filter((f) => f.status === 'loaded').map((f) => `${f.family} ${f.weight}`));
if (!loaded.some((f) => f.startsWith('Overpass Mono'))) {
  console.error('The fonts did not load; the images would use a fallback font. Is there a network?');
  process.exit(1);
}

await page.evaluate(() => {
  for (const tab of document.querySelectorAll('g.message')) {
    const widest = Math.max(...[...tab.querySelectorAll('tspan')].map((t) => t.getComputedTextLength()));
    const width = Math.max(64, Math.ceil(widest) + 28);
    const rect = tab.querySelector('rect');
    rect.setAttribute('width', width);
    rect.setAttribute('x', Number(tab.dataset.cx) - width / 2);
  }
});

for (const f of files) {
  const name = path.basename(f, '.svg');
  await page.locator(`.item[data-name="${name}"] svg`).screenshot({ path: path.join(outDir, `${name}.png`), omitBackground: true });
}
console.log(`${files.length} images in ${outDir}`);
await browser.close();
