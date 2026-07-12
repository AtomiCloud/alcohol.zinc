import { Button, Section } from '@react-email/components';

interface CallToActionProps {
  buttonText: string;
  buttonUrl: string;
}

/**
 * THE LazyTax brand CTA: the orange→fuchsia→violet gradient pill from the
 * argon landing Hero/FinalCTA. bg-brand supplies the solid orange
 * background-color fallback for clients that strip background-image (Outlook).
 */
export const CallToAction = ({ buttonText, buttonUrl }: CallToActionProps) => {
  return (
    <Section className="text-center my-6">
      <Button
        href={buttonUrl}
        className="bg-brand [background-image:linear-gradient(to_right,#f97316,#d946ef,#7c3aed)] text-white font-semibold text-[15px] no-underline rounded-xl px-8 py-3.5 min-w-[240px] inline-block"
      >
        {buttonText}
      </Button>
    </Section>
  );
};
