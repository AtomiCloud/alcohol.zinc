import { Button, Section } from '@react-email/components';

interface CallToActionProps {
  buttonText: string;
  buttonUrl: string;
}

/**
 * The existing LazyTax gradient action, with a solid orange fallback.
 * Keep the pill within its container and allow long labels to wrap rather
 * than imposing a minimum width on narrow email layouts.
 */
export const CallToAction = ({ buttonText, buttonUrl }: CallToActionProps) => {
  return (
    <Section className="text-center my-6">
      <Button
        href={buttonUrl}
        className="bg-brand [background-image:linear-gradient(to_right,#f97316,#d946ef,#7c3aed)] text-white font-semibold text-[16px] leading-[1.5] no-underline rounded-xl px-8 py-3.5 inline-block max-w-full box-border"
      >
        {buttonText}
      </Button>
    </Section>
  );
};
