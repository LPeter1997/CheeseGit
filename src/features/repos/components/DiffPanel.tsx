import { useDiffStore } from "../../diff/store";
import { FileViewer } from "../../diff/components/FileViewer";

export function DiffPanel() {
  const selectedFile = useDiffStore((s) => s.selectedFile);
  const fileContent = useDiffStore((s) => s.fileContent);
  const loading = useDiffStore((s) => s.loading);

  if (!selectedFile) {
    return (
      <div className="flex h-full items-center justify-center text-sm text-fg-muted">
        Select a file to view its contents.
      </div>
    );
  }

  if (loading) {
    return (
      <div className="flex h-full items-center justify-center text-sm text-fg-muted">
        Loading…
      </div>
    );
  }

  if (fileContent === null) {
    return (
      <div className="flex h-full items-center justify-center text-sm text-fg-muted">
        Unable to read file contents.
      </div>
    );
  }

  return <FileViewer filePath={selectedFile} content={fileContent} />;
}
