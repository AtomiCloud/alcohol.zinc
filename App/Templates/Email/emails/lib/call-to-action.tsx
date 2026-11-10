import { Button, Section } from '@react-email/components';

interface CallToActionProps {
  buttonText: string;
  buttonUrl: string;
}

/**
 * A solid, high-contrast action that still works when a mail client removes
 * stylesheets or does not support rounded corners. No fixed minimum width:
 * long labels can wrap on narrow screens instead of widening the email.
 */
export const CallToAction = ({ buttonText, buttonUrl }: CallToActionProps) => {
  return (
    <Section className="my-6">
      <Button
        href={buttonUrl}
        className="bg-brand text-white font-semibold text-[16px] leading-[1.5] no-underline rounded-full block text-center px-3 py-3.5"
      >
        {buttonText}
      </Button>
    </Section>
  );
};
