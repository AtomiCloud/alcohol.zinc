import { Text, Heading, Section } from '@react-email/components';
import { EmailLayout } from './lib/layout';
import { CallToAction } from './lib/call-to-action';
import { TrustBanner } from './lib/trust-banner';

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
  const subject = 'Thank You for Being a LazyTax.Club Member! 🎉';
  const previewText = `Hi ${userName}, thank you for being a valued member of LazyTax.Club!`;

  const trustFeatures = ['Expert Tax Guidance', 'Time-Saving Solutions', 'Member-Only Resources', 'Priority Support'];

  return (
    <EmailLayout
      baseUrl={baseUrl}
      supportEmail={supportEmail}
      whatsappUrl={whatsappUrl}
      telegramUrl={telegramUrl}
      subject={subject}
      previewText={previewText}
      userEmail={userEmail}
    >
      <Heading
        as="h1"
        className="text-2xl sm:text-3xl font-black text-gray-900 mb-4 sm:mb-6 mt-0 text-center border-b-4 border-primary pb-4"
      >
        Thank You, {userName}! 🙏
      </Heading>

      <Text className="text-base sm:text-lg text-gray-900 font-semibold mb-4 sm:mb-6 text-center">
        We're grateful to have you as a valued member of LazyTax.Club! Your trust means the world to us.
      </Text>

      <Section className="bg-gray-200 border-4 border-primary rounded-lg p-4 sm:p-6 mb-4 sm:mb-6">
        <Text className="text-center text-gray-900 font-bold mb-2 mt-0 text-sm sm:text-base">
          <strong>Member Since:</strong> {memberSince || 'Recently joined'}
        </Text>
        {membershipType && (
          <Text className="text-center text-gray-900 font-bold mt-0 mb-0 text-sm sm:text-base">
            <strong>Membership:</strong> {membershipType}
          </Text>
        )}
      </Section>

      <Text className="text-sm sm:text-base text-gray-900 font-semibold mb-4">
        As a LazyTax.Club member, you're part of an exclusive community that makes tax management simple, efficient, and
        stress-free. We're committed to helping you save time and maximize your tax benefits.
      </Text>

      <Text className="text-sm sm:text-base text-gray-900 font-bold mb-4 sm:mb-6">
        Here's what you can continue to enjoy as our member:
      </Text>

      <Section className="mb-4 sm:mb-6 bg-gray-100 border-2 border-gray-800 rounded-lg p-4">
        <ul className="list-none p-0 m-0">
          <li className="mb-3">
            <Text className="text-gray-900 m-0 text-sm sm:text-base font-semibold">
              ✅ <strong>Expert Tax Guidance</strong> - Access to professional tax advice and strategies
            </Text>
          </li>
          <li className="mb-3">
            <Text className="text-gray-900 m-0 text-sm sm:text-base font-semibold">
              ⏰ <strong>Time-Saving Tools</strong> - Automated solutions that handle the heavy lifting
            </Text>
          </li>
          <li className="mb-3">
            <Text className="text-gray-900 m-0 text-sm sm:text-base font-semibold">
              📚 <strong>Exclusive Resources</strong> - Member-only guides, templates, and tax updates
            </Text>
          </li>
          <li className="mb-3">
            <Text className="text-gray-900 m-0 text-sm sm:text-base font-semibold">
              🎯 <strong>Priority Support</strong> - Fast, personalized assistance when you need it
            </Text>
          </li>
        </ul>
      </Section>

      <TrustBanner features={trustFeatures} variant="success" />

      <CallToAction
        title="Explore Your Member Dashboard"
        description="Access all your exclusive member benefits and resources"
        buttonText="Go to Dashboard"
        buttonUrl={`${baseUrl}/dashboard`}
        variant="primary"
      />

      <Section className="bg-accent border-4 border-gray-900 rounded-lg p-3 sm:p-4 mb-4 sm:mb-6">
        <Text className="text-xs sm:text-sm text-white font-bold mb-2 mt-0">
          💡 <strong>Pro Tip:</strong> Bookmark your member dashboard for quick access to all your tax tools and
          resources.
        </Text>
      </Section>

      <Text className="text-sm sm:text-base text-gray-900 font-semibold mb-4">
        We're constantly working to improve your experience and add new features that make tax management even easier.
        Your feedback is always welcome and helps us serve you better.
      </Text>

      <Text className="text-sm sm:text-base text-gray-900 font-bold mb-4 sm:mb-6">
        Thank you for choosing LazyTax.Club and for trusting us with your tax needs. We're here to make your tax journey
        as smooth and successful as possible! 🚀
      </Text>

      <Text className="text-sm sm:text-base text-gray-900 font-semibold mb-2">Best regards,</Text>
      <Text className="text-sm sm:text-base text-gray-900 font-black">The LazyTax.Club Team</Text>
    </EmailLayout>
  );
};

// Preview props for development
MemberThankYouEmail.PreviewProps = {
  baseUrl: 'https://lazytax.club',
  userName: 'Sarah Johnson',
  userEmail: 'sarah@example.com',
  supportEmail: 'support@lazytax.club',
  whatsappUrl: 'https://wa.me/1234567890',
  telegramUrl: 'https://t.me/lazytaxclub',
  memberSince: 'January 2024',
  membershipType: 'Premium Member',
} as MemberThankYouEmailProps;

export default MemberThankYouEmail;
