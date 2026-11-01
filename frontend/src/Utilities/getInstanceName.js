// The server still defaults the instance name to "Readarr"; show the product's real name until a custom one is set in
// Settings > General.
export default function getInstanceName() {
  const { instanceName } = window.Readarr;

  return !instanceName || instanceName === 'Readarr' ? 'Bookshelf' : instanceName;
}
