import axios from 'axios';

export async function fetchOrders(): Promise<unknown> {
  return (await axios.get('/api/orders')).data;
}
