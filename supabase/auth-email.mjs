// Run with a template name and paste the generated subject and HTML into Supabase Auth.
const templates = {
  confirmation: ['Confirm your email', 'Confirm your email address to activate your Krono Agent account.', 'Confirm email'],
  invite: ['You’re invited to Krono Agent', 'Accept the invitation to set up your account.', 'Accept invitation'],
  magic_link: ['Your sign-in link', 'Open this one-time link to sign in to Krono Agent.', 'Sign in'],
  email_change: ['Confirm your email change', 'Confirm the change to the email address on your Krono Agent account.', 'Confirm change'],
  recovery: ['Reset your password', 'Use this link to choose a new password for your Krono Agent account.', 'Reset password'],
  reauthentication: ['Your verification code', 'Enter this code in Krono Agent to continue.', null, '{{ .Token }}'],
  password_changed: ['Your password changed', 'The password for your Krono Agent account was changed.'],
  email_changed: ['Your email address changed', 'The email address on your Krono Agent account was changed.'],
  phone_changed: ['Your phone number changed', 'The phone number on your Krono Agent account was changed.'],
  identity_linked: ['A sign-in method was added', 'A new sign-in method was connected to your Krono Agent account.'],
  identity_unlinked: ['A sign-in method was removed', 'A sign-in method was disconnected from your Krono Agent account.'],
  mfa_factor_enrolled: ['A verification method was added', 'A verification method was added to your Krono Agent account.'],
  mfa_factor_unenrolled: ['A verification method was removed', 'A verification method was removed from your Krono Agent account.'],
};

const template = Object.hasOwn(templates, process.argv[2]) ? templates[process.argv[2]] : null;
if (!template) {
  console.error('Choose a template: ' + Object.keys(templates).join(', '));
  process.exit(1);
}
const [title, intro, action, code] = template;
const followup = action || code
  ? 'If you did not request this, you can ignore this email.'
  : 'If you did not make this change, review your account security.';
const content = action
  ? `<a class="krono-action" href="{{ .ConfirmationURL }}" style="display:inline-block;background:#3b9f72;border-radius:16px;padding-bottom:4px;text-decoration:none"><span class="krono-action-face" style="display:block;background:#84e6b1;background-image:linear-gradient(#a4f0c8,#69d6a0);color:#10231f;border-radius:16px;padding:10px 18px;font-family:Rubik,Arial,sans-serif;font-size:14px;font-weight:600;line-height:20px">${action}</span></a>`
  : code
    ? `<p style="font:700 24px monospace;letter-spacing:.18em;color:#84e6b1">${code}</p>`
    : '';

console.log(`Subject: ${title} · Krono Agent

<!doctype html>
<html lang="en">
<head><meta name="viewport" content="width=device-width,initial-scale=1">
<style>
@font-face{font-family:Rubik;src:url('https://kronoagent.github.io/Krono_Agent/fonts/Rubik.ttf') format('truetype');font-weight:100 900;font-display:swap}
@font-face{font-family:'Space Grotesk';src:url('https://kronoagent.github.io/Krono_Agent/fonts/SpaceGrotesk.ttf') format('truetype');font-weight:300 700;font-display:swap}
.krono-action-face{transition:transform 100ms ease-out,filter 100ms ease-out}
.krono-action:hover .krono-action-face{transform:translateY(-2px);filter:brightness(1.06)}
.krono-action:active .krono-action-face{transition:none;transform:translateY(2px);filter:brightness(.92)}
.krono-action:focus-visible{outline:2px solid #a4f0c8;outline-offset:3px}
@media(prefers-reduced-motion:reduce){.krono-action-face{transition:none}.krono-action:hover .krono-action-face,.krono-action:active .krono-action-face{transform:none}}
</style></head>
<body style="margin:0;background:#10151d;color:#f4f8f6;font-family:Rubik,Arial,sans-serif">
  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" bgcolor="#10151d">
    <tr><td align="center" style="padding:32px 16px">
      <table role="presentation" width="100%" cellpadding="0" cellspacing="0" bgcolor="#1b2532" style="max-width:520px;border:1px solid #344452;border-radius:20px">
        <tr><td style="padding:30px">
          <img src="https://kronoagent.github.io/Krono_Agent/krono-mark.png" width="44" height="44" alt="" style="display:block;margin-bottom:12px">
          <div style="color:#84e6b1;font-size:13px;letter-spacing:.14em;font-weight:bold">KRONO AGENT</div>
          <h1 style="margin:16px 0 10px;color:#f4f8f6;font-family:'Space Grotesk',Rubik,Arial,sans-serif;font-size:27px;line-height:1.2">${title}</h1>
          <p style="margin:0 0 22px;color:#d2dce5;font-size:16px;line-height:1.6">${intro}</p>
          ${content}
          <p style="margin:24px 0 0;padding-top:16px;border-top:1px solid #344452;color:#aebed0;font-size:13px;line-height:1.6">${followup}</p>
        </td></tr>
      </table>
    </td></tr>
  </table>
</body>
</html>`);
