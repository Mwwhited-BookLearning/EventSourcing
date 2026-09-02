using System.Text;
using Microsoft.Playwright;

namespace EventStore.E2ETests;

// The screenshot-capture-to-markdown-assembly pipeline this whole project
// exists for (TODO.md, direct request: "scripted so these can be updated
// and extended as needed" -- re-running the owning [TestMethod] via
// `dotnet test` regenerates the playbook end to end, no manual assembly
// step). RecordStepAsync takes a screenshot and a human-readable caption
// at each meaningful point in a real Playwright walkthrough;
// WriteMarkdownAsync assembles everything captured so far into one
// playbook file, embedding each screenshot next to its caption, in the
// order steps were recorded.
//
// Naming convention (direct request, confirmed with the user):
// {workflow}-{feature doc name}.md, e.g. docs/playbooks/vitals/
// workflow-a-patient-enrollment-and-informed-consent.md -- reuses each
// domain README's own existing Workflow lettering (Vitals A-D, Meridian
// A-C) rather than inventing a new "epic" concept this project doesn't
// otherwise have. Screenshots live in a sibling folder matching the
// markdown file's own basename (e.g. .../workflow-a-patient-enrollment-
// and-informed-consent/step-01.png), so the whole playbook -- prose and
// images together -- is one self-contained pair to add, move, or delete.
public class PlaybookRecorder
{
    private readonly string _markdownPath;
    private readonly string _assetDirectoryName;
    private readonly string _assetDirectoryPath;
    private readonly List<Entry> _entries = [];

    // A playbook is screenshot-steps only; style-guide.md (direct request,
    // reusing this exact mechanism rather than a bespoke writer) also needs
    // prose sections with no screenshot of their own (design tokens,
    // accessibility conventions -- already visible across every OTHER
    // section's own screenshot, not a screen in their own right). One
    // ordered list of either kind keeps assembly order = insertion order
    // for both writers, rather than forcing all prose after all screenshots.
    private abstract record Entry;
    private sealed record ScreenshotEntry(string FileName, string Caption) : Entry;
    private sealed record ProseEntry(string Heading, string Markdown) : Entry;

    public PlaybookRecorder(string markdownPath)
    {
        _markdownPath = markdownPath;
        _assetDirectoryName = Path.GetFileNameWithoutExtension(markdownPath);
        _assetDirectoryPath = Path.Combine(Path.GetDirectoryName(markdownPath)!, _assetDirectoryName);
    }

    public async Task RecordStepAsync(IPage page, string caption)
    {
        Directory.CreateDirectory(_assetDirectoryPath);
        var fileName = $"step-{_entries.OfType<ScreenshotEntry>().Count() + 1:D2}.png";
        // FullPage: true -- a viewport-only screenshot silently crops
        // whatever's currently scrolled out of view. Found only by
        // actually reviewing a real screenshot: the Lineage & Playback
        // panel's own reconstructed-data result rendered successfully
        // (the test's own ToBeVisibleAsync assertion passed -- Playwright's
        // "visible" doesn't require being within the current scroll
        // position) but sat entirely below the fold on a page that grew
        // taller than one viewport, so the captured image never showed it
        // at all despite the step genuinely having worked.
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(_assetDirectoryPath, fileName), FullPage = true });
        _entries.Add(new ScreenshotEntry(fileName, caption));
    }

    // A prose-only section, positioned in the file wherever it's added
    // relative to RecordStepAsync calls -- no screenshot of its own, no
    // "Step N" numbering (that would misleadingly imply a procedure).
    public void AddSection(string heading, string markdown) =>
        _entries.Add(new ProseEntry(heading, markdown));

    // Optional -- a PlantUML sequence diagram showing the general
    // business-process flow this playbook's own screenshots walk
    // through (with alt/opt blocks for a real alternate path, e.g. a
    // rejection, a step-up challenge, or an unmatched-vs-matched
    // branch), placed right after the generated-by note and before
    // Step 1. Written by hand per playbook test (direct request,
    // TODO.md-adjacent) rather than derived from the screenshots
    // themselves -- there's no mechanical way to produce a sequence
    // diagram from a page's own DOM, and the underlying business
    // process is exactly the kind of thing a human author decides how
    // to depict, the same reasoning every other PlantUML diagram in
    // this repo is hand-authored rather than generated. Follows this
    // repo's own standing PlantUML convention (CLAUDE.md): no external
    // `!include`, ever.
    public async Task WriteMarkdownAsync(string title, string? sequenceDiagramPlantUml = null)
    {
        if (_entries.Count == 0)
            throw new InvalidOperationException("No steps/sections recorded -- RecordStepAsync or AddSection must be called at least once before WriteMarkdownAsync.");

        var sb = new StringBuilder();
        sb.AppendLine($"# {title}");
        sb.AppendLine();
        sb.AppendLine("_Generated by `EventStore.E2ETests` against a real running deployment --" +
            " re-run `dotnet test tests/EventStore.E2ETests` to regenerate. Don't hand-edit this" +
            " file directly; edit the owning test's own step captions instead, or the content" +
            " will be overwritten on the next run._");
        sb.AppendLine();

        if (sequenceDiagramPlantUml is not null)
        {
            sb.AppendLine("## Sequence Diagram");
            sb.AppendLine();
            // TODO.md's own tracked gap, closed here: scripts/extract-
            // diagrams.mjs inserts this exact `![...](....svg)` reference
            // line immediately above a fenced diagram the FIRST time it
            // runs, but this method REGENERATES the whole file from
            // scratch on every E2E test run, silently dropping that
            // insertion on the next regeneration -- the two pipelines were
            // never coordinated. Emitting it here instead, computed to the
            // exact same path/naming convention that script's own
            // `processFile` uses, means a later `extract-diagrams.mjs` run
            // recognizes it via that script's own idempotency check
            // (its nearest-non-blank-line-before-the-fence match) and
            // never re-inserts or duplicates it -- this file no longer
            // needs re-running just to restore a reference this method
            // itself can already emit correctly, every time.
            var svgReference = ComputeSequenceDiagramSvgReference();
            if (svgReference is not null)
            {
                sb.AppendLine($"![Sequence Diagram]({svgReference})");
                sb.AppendLine();
            }
            sb.AppendLine("```plantuml");
            sb.AppendLine(sequenceDiagramPlantUml.Trim());
            sb.AppendLine("```");
            sb.AppendLine();
        }

        var stepNumber = 0;
        foreach (var entry in _entries)
        {
            switch (entry)
            {
                case ScreenshotEntry(var fileName, var caption):
                    stepNumber++;
                    sb.AppendLine($"## Step {stepNumber}. {caption}");
                    sb.AppendLine();
                    sb.AppendLine($"![{caption}]({_assetDirectoryName}/{fileName})");
                    sb.AppendLine();
                    break;
                case ProseEntry(var heading, var markdown):
                    sb.AppendLine($"## {heading}");
                    sb.AppendLine();
                    sb.AppendLine(markdown.Trim());
                    sb.AppendLine();
                    break;
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_markdownPath)!);
        await File.WriteAllTextAsync(_markdownPath, sb.ToString());
    }

    // Mirrors scripts/extract-diagrams.mjs's own processFile/slugify exactly
    // -- diagramIndex 1 (this method's own single "Sequence Diagram"
    // heading is always the first, and only, fenced diagram this class
    // ever emits), slug "sequence-diagram" (slugify("Sequence Diagram")).
    // A playbook's own path is always docs/playbooks/{domain}/{role}/
    // {task}.md, so the corresponding .puml/.svg pair always lands at
    // docs/diagrams/playbooks/{domain}/{role}/{task}/01-sequence-diagram.
    // {puml,svg} -- computed here purely from _markdownPath's own already-
    // known "...\docs\..." shape (every real caller builds it via
    // Path.Combine(repoRoot, "docs", ...)), no filesystem access needed
    // since the target doesn't need to exist yet (the same "reserve the
    // path before rendering" posture extract-diagrams.mjs's own header
    // comment already documents).
    private string? ComputeSequenceDiagramSvgReference()
    {
        var segments = _markdownPath.Split('\\', '/');
        var docsIndex = Array.LastIndexOf(segments, "docs");
        if (docsIndex < 0 || docsIndex == segments.Length - 1)
            return null; // defensive -- every real caller's own path already contains "docs" with more segments after it

        var relDocSegments = segments[(docsIndex + 1)..]; // e.g. ["playbooks","core","user","go-offline-and-resync.md"]
        var directorySegments = relDocSegments[..^1]; // e.g. ["playbooks","core","user"]
        var fileNameNoExt = Path.GetFileNameWithoutExtension(relDocSegments[^1]); // e.g. "go-offline-and-resync"
        var relDocNoExt = string.Join("/", directorySegments) + "/" + fileNameNoExt;

        var upLevels = string.Concat(Enumerable.Repeat("../", directorySegments.Length));
        return $"{upLevels}diagrams/{relDocNoExt}/01-sequence-diagram.svg";
    }
}
