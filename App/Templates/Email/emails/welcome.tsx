import { Section, Img } from '@react-email/components';
import { EmailLayout } from './lib/layout';
import { Headline, Paragraph, FinePrint } from './lib/typography';
import { ReceiptCard } from './lib/receipt-card';
import { CallToAction } from './lib/call-to-action';

interface WelcomeEmailProps {
  baseUrl: string;
  userName: string;
  supportEmail: string;
}

export const WelcomeEmail = ({
  baseUrl = '{{ baseUrl }}',
  userName = '{{ userName }}',
  supportEmail = '{{ supportEmail }}',
}: WelcomeEmailProps) => {
  return (
    <EmailLayout
      baseUrl={baseUrl}
      supportEmail={supportEmail}
      subject="Welcome to LazyTax — let's build habits that stick"
      previewText="Simple daily check-ins. Stakes that donate to charity. Rewards when you succeed."
    >
      <Section className="text-center mb-3">
        <Img
          src={`${baseUrl}/images/email/mascot.png`}
          alt="The LazyTax panda"
          width="96"
          height="96"
          className="mx-auto"
        />
      </Section>
      <Headline>Welcome to LazyTax, {userName} 👋</Headline>
      <Paragraph>
        Finally, a habit tracker that works. Simple daily check-ins. Optional stakes that donate to charity when you
        miss. Rewards when you succeed.
      </Paragraph>
      <ReceiptCard
        rows={[
          { label: 'Step 1', value: 'Create a tiny habit' },
          { label: 'Step 2', value: 'Stake it (optional)' },
          { label: 'Step 3', value: 'Tap done, daily' },
          { label: 'Miss a day?', value: '100% to your charity*', emphasis: 'emerald' },
        ]}
      />
      <CallToAction buttonText="Start your first habit" buttonUrl={`${baseUrl}/app`} />
      <FinePrint>
        Pro tip from us: start with &ldquo;put on gym clothes&rdquo;, not &ldquo;go to the gym&rdquo;. Tiny habits stick
        — overpromising kills momentum before you start. We&apos;re not your parent, we trust you.
      </FinePrint>
      <FinePrint>
        *100% of your stake minus payment gateway (Airwallex) and donation platform (Pledge.to) processing fees.
      </FinePrint>
    </EmailLayout>
  );
};

WelcomeEmail.PreviewProps = {
  baseUrl: 'https://lazytax.club',
  userName: 'Sarah',
  supportEmail: 'support@lazytax.club',
} as WelcomeEmailProps;

export default WelcomeEmail;
