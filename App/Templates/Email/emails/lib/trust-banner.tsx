import { Section, Text, Row, Column } from '@react-email/components';

interface TrustBannerProps {
  features: string[];
  variant?: 'default' | 'success' | 'info';
}

export const TrustBanner = ({ features, variant = 'default' }: TrustBannerProps) => {
  const variantClasses = {
    default: 'bg-gray-100 border-gray-900',
    success: 'bg-gray-100 border-primary',
    info: 'bg-gray-100 border-secondary',
  };

  return (
    <Section className={`${variantClasses[variant]} border-4 rounded-lg p-4 sm:p-6 my-4 sm:my-6`}>
      <Text className="text-center text-gray-900 font-black mb-4 mt-0 text-sm sm:text-base">
        ✨ Why LazyTax.Club Members Love Us
      </Text>

      <Row>
        {features.map((feature, index) => (
          <Column key={index} className="text-center">
            <Text className="text-xs sm:text-sm text-gray-900 font-bold m-0 p-1 sm:p-2">✓ {feature}</Text>
          </Column>
        ))}
      </Row>
    </Section>
  );
};
