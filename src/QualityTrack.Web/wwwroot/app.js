// Copy buttons: <button data-copy> copies the text of the nearest [data-copy-source].
// Delegated from the document so it keeps working across Blazor's enhanced navigation.
document.addEventListener("click", async (event) => {
  const button = event.target.closest("[data-copy]");
  if (!button) return;

  const source = button.closest("[data-copy-scope]")?.querySelector("[data-copy-source]");
  if (!source) return;

  const label = button.textContent;
  try {
    await navigator.clipboard.writeText(source.textContent);
    button.textContent = "Copiado";
  } catch {
    // Without clipboard permission (or over plain HTTP on another host) the text is selected instead.
    const range = document.createRange();
    range.selectNodeContents(source);
    const selection = window.getSelection();
    selection.removeAllRanges();
    selection.addRange(range);
    button.textContent = "Seleccionado: Ctrl+C";
  }
  setTimeout(() => { button.textContent = label; }, 2000);
});
