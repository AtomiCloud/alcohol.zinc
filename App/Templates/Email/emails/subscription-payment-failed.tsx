import { EmailLayout } from './lib/layout';
import { Headline, Paragraph, FinePrint } from './lib/typography';
import { ReceiptCard } from './lib/receipt-card';
import { CallToAction } from './lib/call-to-action';

interface SubscriptionPaymentFailedEmailProps {
  baseUrl: string;
  userName: string;
  supportEmail: string;
  tier: string;
  amount: string;
  graceEndsDate: string;
}

export const SubscriptionPaymentFailedEmail = ({
  baseUrl = '{{ baseUrl }}',
  userName = '{{ userName }}',
  supportEmail = '{{ supportEmail }}',
  tier = '{{ tier }}',
  amount = '{{ amount }}',
  graceEndsDate = '{{ graceEndsDate }}',
}: SubscriptionPaymentFailedEmailProps) => {
  return (
    <EmailLayout
      baseUrl={baseUrl}
      supportEmail={supportEmail}
      subject="Payment failed — your plan needs attention"
      previewText={`Your ${tier} renewal was declined — you have until ${graceEndsDate}`}
      strip="amber"
    >
      <Headline>
        We couldn&apos;t renew your {tier} plan, {userName}
      </Headline>
      <Paragraph>
        Your renewal charge of {amount} was declined, so your subscription is now in a grace period. You keep full{' '}
        {tier} access while we retry daily.
      </Paragraph>
      <ReceiptCard
        rows={[
          { label: 'Plan', value: tier },
          { label: 'Amount due', value: amount },
          { label: 'Status', value: 'Retrying daily' },
          { label: 'Grace ends', value: graceEndsDate },
        ]}
      />
      <CallToAction buttonText="Update payment method" buttonUrl={`${baseUrl}/billing`} />
      <FinePrint>
        If we can&apos;t collect by {graceEndsDate}, your account moves to the Free plan — your habits and history stay
        safe, and you can resubscribe anytime.
      </FinePrint>
    </EmailLayout>
  );
};

SubscriptionPaymentFailedEmail.PreviewProps = {
  baseUrl: 'https://lazytax.club',
  userName: 'Sarah',
  supportEmail: 'support@lazytax.club',
  tier: 'Pro',
  amount: '$4.99',
  graceEndsDate: '19 July 2026',
} as SubscriptionPaymentFailedEmailProps;

export default SubscriptionPaymentFailedEmail;
