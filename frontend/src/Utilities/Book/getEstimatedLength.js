// There is no stored duration, but every audio file reports its bitrate ("123 kbps"), so length is size / bitrate. It is
// within a few percent for the constant-bitrate AAC files this library holds.
export default function getEstimatedLength(bookFiles) {
  const seconds = (bookFiles || []).reduce((total, file) => {
    const kbps = parseInt(file.mediaInfo && file.mediaInfo.audioBitRate);

    return kbps > 0 ? total + (file.size * 8) / (kbps * 1000) : total;
  }, 0);

  if (!seconds) {
    return '';
  }

  const minutes = Math.round(seconds / 60);

  return `~${Math.floor(minutes / 60)}h ${String(minutes % 60).padStart(2, '0')}m`;
}
