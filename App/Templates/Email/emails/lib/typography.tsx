import { Heading, Text } from '@react-email/components';
import { ReactNode } from 'react';

/** Georgia serif headline in brand navy — matches argon's rendered heading font */
export const Headline = ({ children }: { children: ReactNode }) => (
  <Heading as="h1" className="font-serif text-[23px] leading-[1.3] text-navy font-bold m-0 mb-3 break-words">
    {children}
  </Heading>
);

/** body paragraph — argon slate-700 body convention */
export const Paragraph = ({ children }: { children: ReactNode }) => (
  <Text className="text-[16px] leading-[1.65] text-[#334155] m-0 mb-4 break-words">{children}</Text>
);

/** muted "what happens next" / legal note */
export const FinePrint = ({ children }: { children: ReactNode }) => (
  <Text className="text-[13px] leading-[1.65] text-[#59697b] m-0 mt-4 mb-4 break-words">{children}</Text>
);
