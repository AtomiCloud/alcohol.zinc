import { EmailLayout } from './lib/layout';
import { Headline, Paragraph, FinePrint } from './lib/typography';
import { ReceiptCard } from './lib/receipt-card';
import { CallToAction } from './lib/call-to-action';

interface MemberThankYouEmailProps {
  baseUrl: string;
  userName: string;
  userEmail: string;
  supportEmail: string;
  whatsappUrl?: string;
  telegramUrl?: string;
  memberSince?: string;
  membershipType?: string;
}

export const MemberThankYouEmail = ({
  baseUrl = '{{ baseUrl }}',
  userName = '{{ userName }}',
  userEmail = '{{ userEmail }}',
  supportEmail = '{{ supportEmail }}',
  whatsappUrl = '{{ whatsappUrl }}',
  telegramUrl = '{{ telegramUrl }}',
  memberSince = '{{ memberSince }}',
  membershipType = '{{ membershipType }}',
}: MemberThankYouEmailProps) => {
  return (
    <EmailLayout
      baseUrl={baseUrl}
      supportEmail={supportEmail}
      whatsappUrl={whatsappUrl}
      telegramUrl={telegramUrl}
      subject="Thank you for being a LazyTax member"
      previewText={`Hi ${userName}, thanks for being a valued LazyTax member`}
      userEmail={userEmail}
    >
      <Headline>Thank you, {userName}</Headline>
      <Paragraph>
        We&apos;re grateful to have you as a LazyTax member. You&apos;re part of a community building habits that stick
        — with stakes that do good when they don&apos;t.
      </Paragraph>
      <ReceiptCard
        rows={[
          { label: 'Member since', value: memberSince },
          { label: 'Membership', value: membershipType },
        ]}
      />
      <CallToAction buttonText="Go to your dashboard" buttonUrl={`${baseUrl}/app`} />
      <FinePrint>
        We&apos;re constantly improving LazyTax — your feedback is always welcome and helps us serve you better.
      </FinePrint>
    </EmailLayout>
  );
};

MemberThankYouEmail.PreviewProps = {
  baseUrl: 'https://lazytax.club',
  userName: 'Sarah Johnson',
  userEmail: 'sarah@example.com',
  supportEmail: 'support@lazytax.club',
  whatsappUrl: 'https://wa.me/message/BXGMZ4HV5M32K1',
  telegramUrl: 'https://t.me/lazytax',
  memberSince: 'January 2026',
  membershipType: 'Pro',
} as MemberThankYouEmailProps;

export default MemberThankYouEmail;
