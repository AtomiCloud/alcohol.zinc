import { Section, Row, Column, Text } from '@react-email/components';

export interface ReceiptRow {
  label: string;
  value: string;
  /** emerald = money-to-charity emphasis */
  emphasis?: 'emerald';
}

interface ReceiptCardProps {
  /** prominent amount, Georgia navy — omit for notification-style emails */
  amount?: string;
  /** small uppercase label under the amount */
  amountLabel?: string;
  /** One label/value pair per table row, including in clients without media queries. */
  rows: ReceiptRow[];
}

export const ReceiptCard = ({ amount, amountLabel, rows }: ReceiptCardProps) => {
  return (
    <>
      {amount && (
        <Section className="mt-5 mb-1">
          <Text className="font-serif font-bold text-[38px] leading-[1.2] text-navy m-0 break-words">{amount}</Text>
          {amountLabel && <Text className="text-[12px] leading-[1.5] text-[#59697b] m-0 mt-1">{amountLabel}</Text>}
        </Section>
      )}
      <Section className="bg-cream border border-solid border-line rounded-lg mt-4 mb-1 px-4 py-1">
        {rows.map(row => (
          <Row key={row.label} className="table-fixed w-full">
            <Column className="w-2/5 pr-3 py-2 align-top">
              <Text className="text-[13px] leading-[1.6] text-[#59697b] m-0 break-words">{row.label}</Text>
            </Column>
            <Column className="w-3/5 py-2 align-top">
              <Text
                className={`text-[14px] leading-[1.6] text-right m-0 break-words ${
                  row.emphasis === 'emerald' ? 'text-[#047857] font-bold' : 'text-navy'
                }`}
              >
                {row.value}
              </Text>
            </Column>
          </Row>
        ))}
      </Section>
    </>
  );
};
