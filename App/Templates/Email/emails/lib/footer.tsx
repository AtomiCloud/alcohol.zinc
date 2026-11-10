import { Section, Text, Link, Hr } from '@react-email/components';

interface FooterProps {
  baseUrl: string;
  supportEmail: string;
  whatsappUrl?: string;
  telegramUrl?: string;
  userEmail?: string;
  reason?: string;
}

/**
 * Muted footer echoing the argon site footer: support contact, legal links,
 * registered address. Transactional emails — no unsubscribe management needed.
 */
export const Footer = ({
  baseUrl,
  supportEmail,
  whatsappUrl,
  telegramUrl,
  reason = "You're receiving this because you have a LazyTax account.",
}: FooterProps) => {
  const year = new Date().getFullYear();
  return (
    <Section className="px-6 pb-6">
      <Hr className="border-solid border-line border-t my-0 mb-4" />
      <Text className="text-[12px] leading-[1.65] text-[#59697b] m-0 mb-2 break-words">
        {reason} Questions?{' '}
        <Link href={`mailto:${supportEmail}`} className="text-[#455d7a] underline">
          {supportEmail}
        </Link>
      </Text>
      <Text className="text-[12px] leading-[1.65] text-[#59697b] m-0 mb-2">
        <Link href={`${baseUrl}/legal/privacy`} className="text-[#455d7a] underline">
          Privacy
        </Link>
        {' · '}
        <Link href={`${baseUrl}/legal/terms`} className="text-[#455d7a] underline">
          Terms
        </Link>
        {' · '}
        <Link href={`${baseUrl}/legal/refund`} className="text-[#455d7a] underline">
          Refunds
        </Link>
        {whatsappUrl && (
          <>
            {' · '}
            <Link href={whatsappUrl} className="text-[#455d7a] underline">
              WhatsApp
            </Link>
          </>
        )}
        {telegramUrl && (
          <>
            {' · '}
            <Link href={telegramUrl} className="text-[#455d7a] underline">
              Telegram
            </Link>
          </>
        )}
      </Text>
      <Text className="text-[12px] leading-[1.65] text-[#59697b] m-0">
        © {year} LazyTax · 2108 N ST #15772, Sacramento, CA 95816, USA
      </Text>
    </Section>
  );
};
