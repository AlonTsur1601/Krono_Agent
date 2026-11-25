module.exports = async function authorize(address) {
  const url = new URL(address)
  if (url.origin !== 'https://accounts.google.com' || url.pathname !== '/o/oauth2/auth')
    throw new Error('Unexpected Firebase authorization address')
  process.stdout.write(JSON.stringify({ authorize: url.href }) + '\n')
}
