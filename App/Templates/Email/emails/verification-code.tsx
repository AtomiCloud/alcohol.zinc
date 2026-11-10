import { Section, Text } from '@react-email/components';
import { EmailLayout } from './lib/layout';
import { Headline, Paragraph, FinePrint } from './lib/typography';

interface VerificationCodeEmailProps {
  code: string;
}

// Logto's SMTP connector substitutes only {{code}}. Brand URLs and support
// must therefore be literal values, unlike Zinc's server-rendered templates.
export const VerificationCodeEmail = ({ code = '{{code}}' }: VerificationCodeEmailProps) => (
  <EmailLayout
    baseUrl="https://lazytax.club"
    supportEmail="support@lazytax.club"
    subject="Your LazyTax verification code"
    previewText="Use this one-time code on the verification screen you opened."
    reason="You received this because a verification request used this email address."
  >
    <Headline>Your verification code</Headline>
    <Paragraph>Use this code on the LazyTax verification screen you already opened.</Paragraph>
    <Section className="bg-[#f8fafc] border border-solid border-[#e2e8f0] rounded-lg my-5 px-2 py-5">
      <Text className="font-mono font-bold text-[32px] leading-[1.3] tracking-[4px] text-center text-navy m-0">
        {code}
      </Text>
    </Section>
    <FinePrint>Never share this code with anyone. LazyTax support will never ask you for it.</FinePrint>
    <FinePrint>If you didn&apos;t request this code, you can ignore this email.</FinePrint>
  </EmailLayout>
);

VerificationCodeEmail.PreviewProps = { code: '482619' } as VerificationCodeEmailProps;

export default VerificationCodeEmail;
