import { Section, Img, Link, Text } from '@react-email/components';

interface HeaderProps {
  baseUrl: string;
}

export const Header = ({ baseUrl }: HeaderProps) => {
  return (
    <Section className="bg-primary border-b-4 border-gray-800 px-4 py-4 sm:px-8 sm:py-6">
      <Link href={baseUrl} className="flex items-center justify-center sm:justify-start">
        <Text className="text-xl sm:text-2xl font-black text-white m-0 text-center sm:text-left tracking-wide">
          LazyTax.Club
        </Text>
      </Link>
    </Section>
  );
};
