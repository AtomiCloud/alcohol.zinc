# LazyTax email templates

Templates retain the existing LazyTax design in `App/Templates/Email/emails/lib`:
white cards on a slate background, navy serif headings, gradient actions and
two-column receipt details. The Friendly (B) restyle is deferred. Pixel-based
styling, larger readable text, wrapping fixes and accurate billing copy remain.
Only welcome includes the decorative panda; the small brand logo appears everywhere.

## Build and delivery

Run `task email:build` before building .NET. React Email inlines the Tailwind
styles and exports to `App/Templates/Email/templates/` (ignored build output).
The .NET build embeds that HTML. Production subjects live in
`App/Modules/Notification/EmailTemplates.cs`, not the TSX `subject` prop.

Use `task email:dev` for browser previews. The admin-only email test endpoint is
`POST /api/v1/Email/{template}/{recipient}`. It sends real email; use only with
approval and a nominated test inbox. It supports the eight Zinc notification
templates, not the auth connector described below.

## OTP / Logto

`verification-code.tsx` exports `templates/verification-code.html` using the same
shared layout. It is a generic one-time-code message suitable for Logto SMTP's
`Register`, `SignIn`, `ForgotPassword` and `Generic` usages. Its only runtime
placeholder is `{{code}}`; brand URLs are literal HTTPS URLs because Logto does
not supply Zinc's `baseUrl` or `supportEmail` variables. Code expiration remains
enforced by Logto; the template does not invent a lifetime.

This file is **not automatically installed into Logto by a Zinc deployment**.
The connector's live configuration is stored separately. After confirming the
installed connector and obtaining approval, retain its SMTP credentials,
sender and other settings; update only the relevant existing template entries
with `contentType: "text/html"`, an appropriate subject, and the exported HTML
as `content`. Do not replace the connector's entire templates array: other
usage types or languages may already be configured. Inspect both staging and
production configurations before applying anything.

The SMTP substitution contract was checked against
`AtomiCloud/aldehyde.logto/packages/connectors/connector-smtp/src/types.ts` and
its README; this does not establish which connector is currently installed.

## Compatibility checks

- Keep layout and detail rows table-based; use inline styles from the exporter.
  Core layout must work without media queries, web fonts, JavaScript or images.
- Keep codes and all financial details as text, with meaningful image alt text.
- Test 320px and wider layouts with long names and values, blocked images, and
  removed head styles, as well as desktop rendering.
- Buttons have no fixed minimum width and stay within their container. The
  gradient has a solid orange fallback for clients without gradient support;
  rounded corners may degrade to square corners.
- The layout declares light mode. Gmail and other apps can still force color
  inversion; browser dark preference is not a substitute for real inbox tests.
- Before release, send approved samples to Apple Mail on iPhone, Gmail on
  Android and browser Gmail/Outlook. Confirm wrapping, tap targets, dark mode,
  image blocking, code selection and the actual link destinations. Browser
  previews and WebKit approximation alone do not certify those clients.
