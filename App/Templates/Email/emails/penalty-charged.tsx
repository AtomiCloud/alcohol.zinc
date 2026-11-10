import { Link } from '@react-email/components';
import { EmailLayout } from './lib/layout';
import { Headline, Paragraph, FinePrint } from './lib/typography';
import { ReceiptCard } from './lib/receipt-card';
import { CallToAction } from './lib/call-to-action';

interface PenaltyChargedEmailProps {
  baseUrl: string;
  userName: string;
  supportEmail: string;
  amount: string;
  charityName: string;
  habitName: string;
  chargeDate: string;
}

export const PenaltyChargedEmail = ({
  baseUrl = '{{ baseUrl }}',
  userName = '{{ userName }}',
  supportEmail = '{{ supportEmail }}',
  amount = '{{ amount }}',
  charityName = '{{ charityName }}',
  habitName = '{{ habitName }}',
  chargeDate = '{{ chargeDate }}',
}: PenaltyChargedEmailProps) => {
  return (
    <EmailLayout
      baseUrl={baseUrl}
      supportEmail={supportEmail}
      subject="Your LazyTax habit stake receipt"
      previewText={`Your ${amount} stake is headed to ${charityName}`}
    >
      <Headline>Your stake was charged, {userName}</Headline>
      <Paragraph>
        You missed {habitName}, so your agreed stake was charged and is headed to your chosen charity.
      </Paragraph>
      <ReceiptCard
        amount={amount}
        amountLabel="Stake payment collected"
        rows={[
          { label: 'Charity', value: charityName, emphasis: 'emerald' },
          { label: 'Habit', value: habitName },
          { label: 'Charged on', value: chargeDate },
          { label: 'Donation', value: '100% minus processing fees' },
        ]}
      />
      <CallToAction buttonText="Open your dashboard" buttonUrl={`${baseUrl}/app`} />
      <FinePrint>
        100% of what you paid — minus payment gateway (Airwallex) and donation platform (Pledge.to) fees — goes to{' '}
        {charityName}. Donations are generally non-refundable once disbursed; if this charge was a technical error,
        contact us within 14 days and we&apos;ll make it right.{' '}
        <Link href={`${baseUrl}/legal/refund`} className="text-violet underline">
          Refund policy
        </Link>
      </FinePrint>
    </EmailLayout>
  );
};

PenaltyChargedEmail.PreviewProps = {
  baseUrl: 'https://lazytax.club',
  userName: 'Sarah',
  supportEmail: 'support@lazytax.club',
  amount: '$5.00',
  charityName: 'Doctors Without Borders',
  habitName: 'Morning run',
  chargeDate: '12 July 2026',
} as PenaltyChargedEmailProps;

export default PenaltyChargedEmail;
