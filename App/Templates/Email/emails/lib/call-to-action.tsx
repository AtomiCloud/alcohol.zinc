import { Button, Section, Text } from '@react-email/components';

interface CallToActionProps {
  title: string;
  description?: string;
  buttonText: string;
  buttonUrl: string;
  variant?: 'primary' | 'secondary' | 'accent';
}

export const CallToAction = ({ title, description, buttonText, buttonUrl, variant = 'primary' }: CallToActionProps) => {
  const variantClasses = {
    primary: 'bg-primary text-white border-4 border-gray-900',
    secondary: 'bg-secondary text-white border-4 border-gray-900',
    accent: 'bg-accent text-white border-4 border-gray-900',
  };

  return (
    <Section className="bg-gray-200 border-4 border-gray-800 rounded-lg p-4 sm:p-6 my-4 sm:my-6 text-center">
      <Text className="text-base sm:text-lg font-black text-gray-900 mb-2 mt-0">{title}</Text>

      {description && <Text className="text-sm sm:text-base text-gray-900 font-semibold mb-4 mt-0">{description}</Text>}

      <Button
        href={buttonUrl}
        className={`${variantClasses[variant]} px-4 py-2 sm:px-6 sm:py-3 rounded-lg font-black text-sm inline-block no-underline`}
      >
        {buttonText}
      </Button>
    </Section>
  );
};
