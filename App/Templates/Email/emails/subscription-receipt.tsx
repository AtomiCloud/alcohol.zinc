import { Link } from '@react-email/components';
import { EmailLayout } from './lib/layout';
import { Headline, Paragraph, FinePrint } from './lib/typography';
import { ReceiptCard } from './lib/receipt-card';
import { CallToAction } from './lib/call-to-action';

interface SubscriptionReceiptEmailProps {
  baseUrl: string;
  userName: string;
  supportEmail: string;
  tier: string;
  amount: string;
  chargeDate: string;
  nextBillingDate: string;
}

export const SubscriptionReceiptEmail = ({
  baseUrl = '{{ baseUrl }}',
  userName = '{{ userName }}',
  supportEmail = '{{ supportEmail }}',
  tier = '{{ tier }}',
  amount = '{{ amount }}',
  chargeDate = '{{ chargeDate }}',
  nextBillingDate = '{{ nextBillingDate }}',
}: SubscriptionReceiptEmailProps) => {
  return (
    <EmailLayout
      baseUrl={baseUrl}
      supportEmail={supportEmail}
      subject="Your LazyTax receipt"
      previewText={`Your LazyTax ${tier} subscription is active`}
    >
      <Headline>Thanks, {userName} — here&apos;s your receipt</Headline>
      <Paragraph>
        Your LazyTax {tier} subscription is active. Every subscription keeps the lights on so 100% of stakes can go to
        charity.
      </Paragraph>
      <ReceiptCard
        amount={amount}
        amountLabel={`${tier} subscription`}
        rows={[
          { label: 'Plan', value: tier },
          { label: 'Amount', value: amount },
          { label: 'Date', value: chargeDate },
          { label: 'Next billing', value: nextBillingDate },
        ]}
      />
      <CallToAction buttonText="Manage subscription" buttonUrl={`${baseUrl}/billing`} />
      <FinePrint>
        New here? If it&apos;s not for you, you can request a refund within 7 days of your first purchase. Billing
        errors are corrected up to 30 days back.{' '}
        <Link href={`${baseUrl}/legal/refund`} className="text-violet underline">
          Refund policy
        </Link>
      </FinePrint>
    </EmailLayout>
  );
};

SubscriptionReceiptEmail.PreviewProps = {
  baseUrl: 'https://lazytax.club',
  userName: 'Sarah',
  supportEmail: 'support@lazytax.club',
  tier: 'Pro',
  amount: '$4.99',
  chargeDate: '12 July 2026',
  nextBillingDate: '12 August 2026',
} as SubscriptionReceiptEmailProps;

export default SubscriptionReceiptEmail;
