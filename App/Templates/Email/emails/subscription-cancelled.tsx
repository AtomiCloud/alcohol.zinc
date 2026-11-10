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
      <Paragraph>Your LazyTax subscription has changed. Review the change and its effective date below.</Paragraph>
      <ReceiptCard
        rows={[
          { label: 'Change', value: changeSummary },
          { label: 'Effective', value: effectiveDate },
          { label: 'Refunds', value: 'No partial refunds' },
        ]}
      />
      <CallToAction buttonText="Manage subscription" buttonUrl={`${baseUrl}/billing`} />
      <FinePrint>
        Your habits and history stay safe. You can review your current plan, resubscribe or switch tiers from your
        billing page.
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
