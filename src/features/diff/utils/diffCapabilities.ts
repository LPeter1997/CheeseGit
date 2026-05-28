/**
 * Opaque binary formats that cannot be shown in the diff viewer at all:
 * archives, executables, compiled bytecode, audio/video, fonts, documents.
 */
const OPAQUE_EXTENSIONS = new Set([
  "pdf",
  "woff", "woff2", "ttf", "otf", "eot",
  "zip", "gz", "tar", "bz2", "xz", "7z", "rar",
  "exe", "dll", "so", "dylib", "bin",
  "wasm",
  "mp3", "mp4", "ogg", "wav", "flac", "avi", "mkv", "mov", "webm",
  "class", "jar", "pyc", "pyo",
  "ds_store",
]);

/**
 * Image/icon formats. Currently treated as non-displayable alongside opaque
 * binaries, but kept separate so a future "before/after image" diff view can
 * enable `canDisplayDiff` for them while `canSearchDiff` stays false.
 */
const IMAGE_EXTENSIONS = new Set([
  "png", "jpg", "jpeg", "gif", "bmp", "ico", "icns", "webp", "avif",
  "tiff", "tif", "svg",
  "psd", "sketch",
]);

function getExt(filePath: string): string {
  return filePath.split(".").pop()?.toLowerCase() ?? "";
}

/**
 * Returns true when the file can be rendered in the diff viewer.
 * Currently requires the file to be a plain-text format.
 * In the future this will also return true for image files once
 * a before/after image view is implemented.
 */
export function canDisplayDiff(filePath: string): boolean {
  const ext = getExt(filePath);
  return !OPAQUE_EXTENSIONS.has(ext) && !IMAGE_EXTENSIONS.has(ext);
}

/**
 * Returns true when the diff viewer supports text search for this file.
 * A superset of the `canDisplayDiff` restriction: image diffs (future) will
 * be displayable but not text-searchable.
 */
export function canSearchDiff(filePath: string): boolean {
  const ext = getExt(filePath);
  return !OPAQUE_EXTENSIONS.has(ext) && !IMAGE_EXTENSIONS.has(ext);
}
