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
const buttonWidths = { confirmation: 158, invite: 185, magic_link: 150, email_change: 174, recovery: 174 };
const content = action
  ? `<a class="krono-action" href="{{ .ConfirmationURL }}" style="display:inline-block;color:#84e6b1;text-decoration:none;font-family:Rubik,Arial,sans-serif;font-weight:600"><img src="https://alontsur1601.github.io/Krono_Agent/email/${process.argv[2]}.gif" width="${buttonWidths[process.argv[2]]}" height="58" alt="${action}" style="display:block;border:0;max-width:100%;height:auto;color:#84e6b1;font-family:Rubik,Arial,sans-serif;font-size:16px"></a>`
  : code
    ? `<p style="font:700 24px monospace;letter-spacing:.18em;color:#84e6b1">${code}</p>`
    : '';

console.log(`Subject: ${title} · Krono Agent

<!doctype html>
<html lang="en">
<head><meta name="viewport" content="width=device-width,initial-scale=1">
<style>
@font-face{font-family:Rubik;src:url('https://alontsur1601.github.io/Krono_Agent/fonts/Rubik.ttf') format('truetype');font-weight:100 900;font-display:swap}
@font-face{font-family:'Space Grotesk';src:url('https://alontsur1601.github.io/Krono_Agent/fonts/SpaceGrotesk.ttf') format('truetype');font-weight:300 700;font-display:swap}
.krono-action{transition:transform .18s ease}
.krono-action:hover{transform:translateY(-2px)}
.krono-action:active{transform:translateY(2px)}
@media(prefers-reduced-motion:reduce){.krono-action{transition:none}}
</style></head>
<body style="margin:0;background:#10151d;color:#f4f8f6;font-family:Rubik,Arial,sans-serif">
  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" bgcolor="#10151d">
    <tr><td align="center" style="padding:32px 16px">
      <table role="presentation" width="100%" cellpadding="0" cellspacing="0" bgcolor="#1b2532" style="max-width:520px;border:1px solid #344452;border-radius:20px">
        <tr><td style="padding:30px">
          <img src="https://alontsur1601.github.io/Krono_Agent/krono-mark.png" width="44" height="44" alt="" style="display:block;margin-bottom:12px">
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
