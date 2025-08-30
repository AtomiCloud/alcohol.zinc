# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Development Commands

### Email Template Development

- `bun dev` or `npm run dev` - Start React Email dev server for live preview
- `bun build` or `npm run build` - Build email templates
- `bun export` or `npm run export` - Export templates to static HTML

### Project Management

- `task runbook` - Run the main runbook application
- `task setup` - Setup local development environment

## React Email Architecture

This project uses **React Email 4.2.8** with **Tailwind CSS** integration for building email templates. The email system consists of:

### Frontend (TypeScript/React)

- **Email Templates**: Located in `emails/` directory
- **Component Library**: Reusable components in `emails/lib/` for:
  - Layout management (`layout.tsx`)
  - Header/Footer components
  - Booking details components
  - Trust banners and CTAs
- **Index File**: `emails/index.ts` exports all templates and components

### Backend (C#)

- **EmailRenderer**: Renders templates using Handlebars.NET
- **SMTP Services**: Email delivery infrastructure
- **Template Loading**: Loads from embedded resources with caching

### Key React Email Dependencies

- `@react-email/components@0.5.0` - Core components
- `@react-email/tailwind@^1.2.2` - Tailwind integration
- `@react-email/preview-server@4.2.8` - Development server

## Email Template Development Guidelines

### Always Use Tailwind CSS

This project **exclusively uses Tailwind CSS** for styling. Key patterns:

```tsx
import { Tailwind, Button, Text } from '@react-email/components';

// Always wrap components in Tailwind provider
<Tailwind
  config={{
    theme: {
      extend: {
        colors: {
          brand: '#667eea',
        },
      },
    },
  }}
>
  <Button className="bg-brand px-4 py-2 text-white rounded">Click me</Button>
</Tailwind>;
```

### Template Structure Pattern

All email templates follow this structure:

```tsx
interface TemplateProps {
  baseUrl: string;
  userName: string;
  userEmail: string;
  supportEmail: string;
  // ... other props
}

export const TemplateEmail = ({
  baseUrl = '{{ baseUrl }}', // Handlebars placeholders for C# rendering
  userName = '{{ userName }}',
  supportEmail = '{{ supportEmail }}',
  // ... defaults
}: TemplateProps) => {
  return (
    <EmailLayout baseUrl={baseUrl} supportEmail={supportEmail} subject="Template Subject" previewText="Preview text">
      {/* Template content */}
    </EmailLayout>
  );
};

// Required for development preview
TemplateEmail.PreviewProps = {
  baseUrl: 'https://example.com',
  userName: 'John Doe',
  supportEmail: 'support@example.com',
  // ... sample data
} as TemplateProps;
```

### Component Guidelines

- **Use EmailLayout**: Wrap all templates in the `EmailLayout` component
- **Handlebars Integration**: Use `{{ variableName }}` syntax for server-side rendering
- **PreviewProps**: Always include `.PreviewProps` for development preview
- **Email-Safe Components**: Only use React Email components, never native HTML elements
- **Responsive Design**: Use Tailwind responsive classes carefully (email client support varies)

### Styling Best Practices

- **Tailwind Only**: Never use inline styles or CSS modules
- **Email-Safe Classes**: Prefer basic Tailwind classes over complex utilities
- **Color Consistency**: Use theme colors defined in Tailwind config
- **Typography**: Use React Email's `Text` and `Heading` components with Tailwind classes

### Email Client Compatibility

- Test templates work across Gmail, Outlook, Apple Mail, Yahoo! Mail
- Avoid complex Tailwind selectors (pseudo-classes, complex combinators)
- Use pixel-based measurements where possible
- Prefer table-based layouts over flexbox/grid

### Development Workflow

1. Create template in `emails/` directory
2. Add component exports to `emails/index.ts`
3. Use `bun dev` to preview in browser
4. Test across email clients
5. Templates are compiled to HTML by C# backend using Handlebars

### Template Data Flow

1. C# backend loads templates from embedded resources
2. Uses Handlebars.NET for variable substitution
3. React Email exports static HTML templates
4. EmailRenderer service handles caching and rendering

## Email Service Integration

- SMTP services configured in C# backend
- Multiple provider support (configured via appsettings)
- Template rendering cached for performance
- Variables injected via Handlebars templating engine
