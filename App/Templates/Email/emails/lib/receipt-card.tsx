import { Section, Row, Column, Text } from '@react-email/components';

export interface ReceiptRow {
  label: string;
  value: string;
  /** emerald = money-to-charity emphasis */
  emphasis?: 'emerald';
}

interface ReceiptCardProps {
  /** big centered amount, Georgia navy — omit for notification-style emails */
  amount?: string;
  /** small uppercase label under the amount */
  amountLabel?: string;
  /** key–value cells, Apple-receipt style; rendered 2 per row */
  rows: ReceiptRow[];
}

const chunk = <T,>(xs: T[], size: number): T[][] =>
  xs.reduce<T[][]>((acc, x, i) => {
    if (i % size === 0) acc.push([]);
    acc[acc.length - 1].push(x);
    return acc;
  }, []);

export const ReceiptCard = ({ amount, amountLabel, rows }: ReceiptCardProps) => {
  return (
    <>
      {amount && (
        <Section className="text-center mt-5 mb-1">
          <Text className="font-serif font-bold text-[40px] leading-[1.1] text-navy m-0">{amount}</Text>
          {amountLabel && (
            <Text className="text-[11px] tracking-[1.4px] uppercase text-[#64748b] m-0 mt-1">{amountLabel}</Text>
          )}
        </Section>
      )}
      <Section className="bg-[#f8fafc] border border-solid border-[#e2e8f0] rounded-lg mt-4 mb-1 px-0 py-1">
        {chunk(rows, 2).map((pair, i) => (
          <Row key={i}>
            {pair.map(row => (
              <Column key={row.label} className="w-1/2 px-4 py-2 align-top">
                <Text className="text-[10px] tracking-[1.2px] uppercase text-[#64748b] m-0 mb-0.5">{row.label}</Text>
                <Text
                  className={`text-[13.5px] m-0 ${
                    row.emphasis === 'emerald' ? 'text-emerald font-bold' : 'text-[#0f172a] font-medium'
                  }`}
                >
                  {row.value}
                </Text>
              </Column>
            ))}
            {pair.length === 1 && <Column className="w-1/2 px-4 py-2">&nbsp;</Column>}
          </Row>
        ))}
      </Section>
    </>
  );
};
