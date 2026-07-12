import { EmailLayout } from './lib/layout';
import { Headline, Paragraph, FinePrint } from './lib/typography';
import { ReceiptCard } from './lib/receipt-card';
import { CallToAction } from './lib/call-to-action';

interface PenaltyPaymentFailedEmailProps {
  baseUrl: string;
  userName: string;
  supportEmail: string;
  amount: string;
  charityName: string;
  habitName: string;
  attemptDate: string;
}

export const PenaltyPaymentFailedEmail = ({
  baseUrl = '{{ baseUrl }}',
  userName = '{{ userName }}',
  supportEmail = '{{ supportEmail }}',
  amount = '{{ amount }}',
  charityName = '{{ charityName }}',
  habitName = '{{ habitName }}',
  attemptDate = '{{ attemptDate }}',
}: PenaltyPaymentFailedEmailProps) => {
  return (
    <EmailLayout
      baseUrl={baseUrl}
      supportEmail={supportEmail}
      subject="We couldn't process your stake"
      previewText={`Your ${amount} stake payment was declined`}
    >
      <Headline>Your stake payment didn&apos;t go through, {userName}</Headline>
      <Paragraph>
        We tried to charge your card for your <b>{habitName}</b> stake, but the payment was declined.
      </Paragraph>
      <ReceiptCard
        amount={amount}
        amountLabel="pending — payment declined"
        rows={[
          { label: 'Charity', value: charityName },
          { label: 'Habit', value: habitName },
          { label: 'Last attempt', value: attemptDate },
          { label: 'Status', value: 'Will retry automatically' },
        ]}
      />
      <CallToAction buttonText="Update payment method" buttonUrl={`${baseUrl}/billing`} />
      <FinePrint>
        What happens next: we&apos;ll retry the charge automatically a few more times. If it still doesn&apos;t go
        through, we&apos;ll stop — we won&apos;t pursue collections or charge late fees. This is about accountability,
        not debt collection. Updating your card is the fastest fix.
      </FinePrint>
    </EmailLayout>
  );
};

PenaltyPaymentFailedEmail.PreviewProps = {
  baseUrl: 'https://lazytax.club',
  userName: 'Sarah',
  supportEmail: 'support@lazytax.club',
  amount: '$5.00',
  charityName: 'Doctors Without Borders',
  habitName: 'Morning run',
  attemptDate: '12 July 2026',
} as PenaltyPaymentFailedEmailProps;

export default PenaltyPaymentFailedEmail;
