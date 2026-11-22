function showConfirmation() {
  const fragment = new URLSearchParams(location.hash.slice(1));
  const query = new URLSearchParams(location.search);
  const error = fragment.has('error_description') || query.has('error_description');
  const callback = error || fragment.has('access_token');
  const result = callback ? (error ? 'expired' : 'confirmed') : location.hash.slice(1);
  if (!callback && !document.documentElement.hasAttribute('data-confirmation')) return;
  if (result !== 'confirmed' && result !== 'expired') return;

  if (callback) history.replaceState(null, '', new URL('confirm.html#' + result, location.href));
  document.title = 'Email confirmation · Krono Agent';
  document.documentElement.dataset.confirmation = result;
  document.querySelector('[data-confirmation-title]').textContent =
    result === 'confirmed' ? 'Email confirmed' : 'Confirmation link expired';
  document.querySelector('[data-confirmation-message]').textContent = result === 'confirmed'
    ? 'You can close this tab and return to Krono Agent.'
    : 'Return to Krono Agent and request a new confirmation email.';
}

showConfirmation();
addEventListener('hashchange', showConfirmation);
