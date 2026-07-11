import { EmailLayout } from './lib/layout';
import { Headline, Paragraph, FinePrint } from './lib/typography';
import { ReceiptCard } from './lib/receipt-card';
import { CallToAction } from './lib/call-to-action';

interface PaymentConsentChangedEmailProps {
  baseUrl: string;
  userName: string;
  supportEmail: string;
  /** composed in C#: "Payment method linked" | "Payment method unlinked" */
  changeSummary: string;
  /** "Habit stakes" | "Subscription" */
  purpose: string;
  changeDate: string;
}

export const PaymentConsentChangedEmail = ({
  baseUrl = '{{ baseUrl }}',
  userName = '{{ userName }}',
  supportEmail = '{{ supportEmail }}',
  changeSummary = '{{ changeSummary }}',
  purpose = '{{ purpose }}',
  changeDate = '{{ changeDate }}',
}: PaymentConsentChangedEmailProps) => {
  return (
    <EmailLayout
      baseUrl={baseUrl}
      supportEmail={supportEmail}
      subject="Your payment method changed"
      previewText={`${changeSummary} for ${purpose}`}
      strip="neutral"
    >
      <Headline>{changeSummary}</Headline>
      <Paragraph>
        Hi {userName} — your payment settings changed. If this was you, there&apos;s nothing else to do. We only charge
        a linked card when you miss a staked habit or when a subscription renews — and 100% of stakes (minus processing
        fees) go to your chosen charity.
      </Paragraph>
      <ReceiptCard
        rows={[
          { label: 'Change', value: changeSummary },
          { label: 'Purpose', value: purpose },
          { label: 'Date', value: changeDate },
          { label: 'Not you?', value: 'Contact support right away' },
        ]}
      />
      <CallToAction buttonText="Manage billing" buttonUrl={`${baseUrl}/billing`} />
      <FinePrint>
        Didn&apos;t make this change? Review your payment settings from the billing page and contact {supportEmail}{' '}
        immediately.
      </FinePrint>
    </EmailLayout>
  );
};

PaymentConsentChangedEmail.PreviewProps = {
  baseUrl: 'https://lazytax.club',
  userName: 'Sarah',
  supportEmail: 'support@lazytax.club',
  changeSummary: 'Payment method linked',
  purpose: 'Habit stakes',
  changeDate: '12 July 2026',
} as PaymentConsentChangedEmailProps;

export default PaymentConsentChangedEmail;
