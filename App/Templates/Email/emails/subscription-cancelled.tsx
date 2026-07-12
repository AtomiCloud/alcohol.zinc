import { EmailLayout } from './lib/layout';
import { Headline, Paragraph, FinePrint } from './lib/typography';
import { ReceiptCard } from './lib/receipt-card';
import { CallToAction } from './lib/call-to-action';

interface SubscriptionCancelledEmailProps {
  baseUrl: string;
  userName: string;
  supportEmail: string;
  /** composed in C#: "Pro → Free", "Ultimate → Pro", "Pro ended (payment not collected)" … */
  changeSummary: string;
  effectiveDate: string;
}

export const SubscriptionCancelledEmail = ({
  baseUrl = '{{ baseUrl }}',
  userName = '{{ userName }}',
  supportEmail = '{{ supportEmail }}',
  changeSummary = '{{ changeSummary }}',
  effectiveDate = '{{ effectiveDate }}',
}: SubscriptionCancelledEmailProps) => {
  return (
    <EmailLayout
      baseUrl={baseUrl}
      supportEmail={supportEmail}
      subject="Your subscription change is confirmed"
      previewText={`${changeSummary} — effective ${effectiveDate}`}
    >
      <Headline>Change confirmed, {userName}</Headline>
      <Paragraph>
        We&apos;ve updated your LazyTax subscription, as requested. No hard feelings — your habits keep working on
        whatever plan you&apos;re on.
      </Paragraph>
      <ReceiptCard
        rows={[
          { label: 'Change', value: changeSummary },
          { label: 'Effective', value: effectiveDate },
          { label: 'Until then', value: 'Full current-plan access' },
          { label: 'Refunds', value: 'No partial refunds' },
        ]}
      />
      <CallToAction buttonText="Manage subscription" buttonUrl={`${baseUrl}/billing`} />
      <FinePrint>
        You keep everything your current plan gives you until {effectiveDate}. Changed your mind? You can resubscribe or
        switch tiers from your billing page — it takes a minute.
      </FinePrint>
    </EmailLayout>
  );
};

SubscriptionCancelledEmail.PreviewProps = {
  baseUrl: 'https://lazytax.club',
  userName: 'Sarah',
  supportEmail: 'support@lazytax.club',
  changeSummary: 'Pro → Free',
  effectiveDate: '12 August 2026',
} as SubscriptionCancelledEmailProps;

export default SubscriptionCancelledEmail;
