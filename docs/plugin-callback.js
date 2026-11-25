const params = new URLSearchParams(location.search)
const state = params.get('state') ?? ''
const code = params.get('code')
const error = params.get('error')
history.replaceState(null, '', location.pathname)

if (/^[a-z0-9_-]{43}$/i.test(state) && (code || error)) {
  const callback = new URL('http://127.0.0.1:54127/callback')
  callback.searchParams.set('state', state)
  if (code) callback.searchParams.set('code', code)
  else callback.searchParams.set('error', error === 'access_denied' ? error : 'authorization_failed')

  document.getElementById('status').textContent = 'Returning to the open Krono Agent window.'
  const link = document.getElementById('return')
  link.href = callback.href
  link.hidden = false
  location.replace(callback.href)
}
