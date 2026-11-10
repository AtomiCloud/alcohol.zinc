import { Section, Row, Column, Img, Link, Text } from '@react-email/components';

interface HeaderProps {
  baseUrl: string;
}

/**
 * Slim logo header, echoing the argon navbar: panda logo + wordmark.
 * The PNG is served from the argon web portal (public/images/email/logo.png) —
 * email clients don't render SVG, so the source logo-source.svg is rasterized there.
 */
export const Header = ({ baseUrl }: HeaderProps) => {
  return (
    <Section className="mb-6">
      <Row>
        <Column className="w-[44px]">
          <Link href={baseUrl}>
            <Img src={`${baseUrl}/images/email/logo.png`} alt="LazyTax" width="36" height="36" className="rounded-lg" />
          </Link>
        </Column>
        <Column>
          <Link href={baseUrl} className="no-underline">
            <Text className="font-serif font-bold text-[19px] text-navy m-0 leading-[36px]">LazyTax</Text>
          </Link>
        </Column>
      </Row>
    </Section>
  );
};
