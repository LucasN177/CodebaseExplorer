import { Order } from './order';
import { formatMoney } from '@acme/shared';
import groupBy from 'lodash/groupBy';
import { log } from '@shared/logging';

// import { ignored } from './not-an-import';
const text = "import { alsoIgnored } from './string-only'";

export class OrderService {
  describe(order: Order): string {
    log(text);
    return formatMoney(order.total) + groupBy([order], 'id');
  }
}

function internalHelper(): void {}
