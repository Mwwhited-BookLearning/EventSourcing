#!/usr/bin/env node
// TODO.md's "Migrate embedded PlantUML diagrams to their own .puml files +
// a Docker rendering pipeline to .svg". One-time (but re-runnable/idempotent)
// migration: walks every docs/**/*.md file, extracts each ```plantuml/```puml
// fenced block into its own .puml file under docs/diagrams/ (mirroring the
// source doc's own relative path), and inserts a rendered-SVG image
// reference immediately above the original fenced block -- which is left
// completely untouched, on purpose. This repo's docs are meant to stay
// readable as plain text with no external tool (CLAUDE.md's own C4-PlantUML
// convention note); deleting the inline source and replacing it with only an
// image reference would regress that. The .puml file is the pipeline's real
// input; the inline fence remains the editable, git-diffable source of truth
// a reader sees inline. Run `node scripts/render-diagrams.mjs` after this to
// actually produce the .svg files this script only reserves the paths for.
import { readFileSync, writeFileSync, mkdirSync, readdirSync, statSync } from "node:fs";
import { join, relative, dirname, sep } from "node:path";

const repoRoot = join(import.meta.dirname, "..");
const docsRoot = join(repoRoot, "docs");
const diagramsRoot = join(docsRoot, "diagrams");

function slugify(text) {
  return text
    .toLowerCase()
    .replace(/[`*_]/g, "")
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .slice(0, 50) || "diagram";
}

function walkMarkdownFiles(dir, out = []) {
  for (const entry of readdirSync(dir)) {
    const full = join(dir, entry);
    if (full.startsWith(diagramsRoot)) continue; // never walk our own output tree
    const stat = statSync(full);
    if (stat.isDirectory()) walkMarkdownFiles(full, out);
    else if (entry.endsWith(".md")) out.push(full);
  }
  return out;
}

function processFile(mdPath) {
  const relDoc = relative(docsRoot, mdPath); // e.g. "features/auth.md"
  const relDocNoExt = relDoc.replace(/\.md$/, "");
  const diagramDir = join(diagramsRoot, relDocNoExt); // docs/diagrams/features/auth/

  const lines = readFileSync(mdPath, "utf8").split("\n");
  const out = [];
  let lastHeading = "diagram";
  let diagramIndex = 0;
  let extractedCount = 0;

  for (let i = 0; i < lines.length; i++) {
    const line = lines[i];
    const headingMatch = /^#{1,6}\s+(.+)$/.exec(line);
    if (headingMatch) lastHeading = headingMatch[1];

    const fenceMatch = /^```(plantuml|puml)\s*$/.exec(line.trim());
    if (!fenceMatch) {
      out.push(line);
      continue;
    }

    // Already migrated (idempotency guard): the nearest non-blank
    // preceding emitted line is our own inserted image reference for this
    // exact fence -- skip a trailing blank line, since we insert one
    // between the image reference and the fence itself.
    let lookback = out.length - 1;
    while (lookback >= 0 && out[lookback].trim() === "") lookback--;
    const alreadyMigrated = lookback >= 0 && /^!\[.*\]\(.*\.svg\)$/.test(out[lookback].trim());

    const bodyLines = [];
    let j = i + 1;
    for (; j < lines.length; j++) {
      if (lines[j].trim() === "```") break;
      bodyLines.push(lines[j]);
    }
    // j now points at the closing fence line.

    diagramIndex++;
    const slug = slugify(lastHeading);
    const pumlName = `${String(diagramIndex).padStart(2, "0")}-${slug}.puml`;
    const pumlPath = join(diagramDir, pumlName);
    const svgPath = pumlPath.replace(/\.puml$/, ".svg");

    // PlantUML names its output after an explicit "@startuml Name"/
    // "@startsalt Name" argument when one is given, not after the source
    // filename -- which would make the .svg's real name unpredictable and
    // break the image reference below. Stripped only in this extracted
    // .puml copy (the pipeline's real input); the inline fence in the
    // markdown, left untouched above, keeps its original named form.
    // With no name argument, PlantUML falls back to naming output after
    // the input file's own basename, which is what makes this predictable.
    const namedBody = bodyLines
      .map((l, idx) => (idx === 0 ? l.replace(/^(@start(?:uml|salt))\s+\S.*$/, "$1") : l))
      .join("\n");

    mkdirSync(diagramDir, { recursive: true });
    writeFileSync(pumlPath, namedBody + "\n", "utf8");
    extractedCount++;

    if (!alreadyMigrated) {
      const relSvg = relative(dirname(mdPath), svgPath).split(sep).join("/");
      const altText = /diagram/i.test(lastHeading) ? lastHeading : `${lastHeading} diagram`;
      out.push(`![${altText}](${relSvg})`);
      out.push("");
    }

    // Emit the original fence and body unchanged.
    out.push(line);
    for (const b of bodyLines) out.push(b);
    out.push(lines[j]); // closing ```
    i = j;
  }

  if (extractedCount > 0) writeFileSync(mdPath, out.join("\n"), "utf8");
  return extractedCount;
}

const files = walkMarkdownFiles(docsRoot);
let totalFiles = 0;
let totalDiagrams = 0;
for (const f of files) {
  const count = processFile(f);
  if (count > 0) {
    totalFiles++;
    totalDiagrams += count;
  }
}
console.log(`Extracted ${totalDiagrams} diagram(s) from ${totalFiles} file(s) into ${relative(repoRoot, diagramsRoot)}/`);
