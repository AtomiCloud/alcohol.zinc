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
  chargeDate: string;
}

export const PenaltyChargedEmail = ({
  baseUrl = '{{ baseUrl }}',
  userName = '{{ userName }}',
  supportEmail = '{{ supportEmail }}',
  amount = '{{ amount }}',
  charityName = '{{ charityName }}',
  chargeDate = '{{ chargeDate }}',
}: PenaltyChargedEmailProps) => {
  return (
    <EmailLayout
      baseUrl={baseUrl}
      supportEmail={supportEmail}
      subject="Your stake went to charity"
      previewText={`Your ${amount} stake is headed to ${charityName}`}
    >
      <Headline>Your stake did some good, {userName}</Headline>
      <Paragraph>
        You missed a staked habit, so your stake was charged and is headed to your chosen charity. That was the deal —
        this isn&apos;t punishment, it&apos;s your commitment made real.
      </Paragraph>
      <ReceiptCard
        amount={amount}
        amountLabel="donated on your behalf"
        rows={[
          { label: 'Charity', value: charityName, emphasis: 'emerald' },
          { label: 'Stake', value: amount },
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
  chargeDate: '12 July 2026',
} as PenaltyChargedEmailProps;

export default PenaltyChargedEmail;
