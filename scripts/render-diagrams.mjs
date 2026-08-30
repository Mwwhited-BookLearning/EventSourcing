#!/usr/bin/env node
// TODO.md's "...a Docker rendering pipeline to .svg". Renders every .puml
// file under docs/diagrams/ to a sibling .svg via the official
// plantuml/plantuml Docker image -- no local Java/PlantUML install needed,
// matching this repo's own "buy over build" convention for tooling. Run
// this after scripts/extract-diagrams.mjs (or after hand-editing any
// existing .puml file) to (re)produce the .svg files the docs' image
// references point at.
//
// Each .puml's first line has its "@startuml/@startsalt Name" argument
// stripped by extract-diagrams.mjs specifically so PlantUML falls back to
// naming output after the input file's own basename -- if a .puml file
// still has a name argument (e.g. hand-added without going through that
// script), PlantUML will write its .svg under that name instead, and this
// script will not find the file at the *.puml-derived path it expects.
import { execFileSync } from "node:child_process";
import { existsSync } from "node:fs";
import { join } from "node:path";

const repoRoot = join(import.meta.dirname, "..");
const diagramsRoot = join(repoRoot, "docs", "diagrams");

if (!existsSync(diagramsRoot)) {
  console.error(`No ${diagramsRoot} directory -- run scripts/extract-diagrams.mjs first.`);
  process.exit(1);
}

// Windows-path form (C:/...), not the Git-Bash /c/... form -- Docker
// Desktop's own volume-mount translation expects this on Windows hosts.
const winRepoRoot = repoRoot.replace(/\\/g, "/");

console.log(`Rendering docs/diagrams/**/*.puml via plantuml/plantuml...`);
execFileSync(
  "docker",
  [
    "run", "--rm",
    "-v", `${winRepoRoot}:/workdir`,
    "plantuml/plantuml",
    "-tsvg",
    "/workdir/docs/diagrams/**/*.puml",
  ],
  {
    stdio: "inherit",
    // MSYS_NO_PATHCONV: Git Bash mangles a leading "/workdir/..." argument
    // into a Windows path before Docker ever sees it, breaking the
    // in-container path -- this is a no-op (and harmless) outside Git Bash.
    env: { ...process.env, MSYS_NO_PATHCONV: "1" },
  },
);
console.log("Done.");
