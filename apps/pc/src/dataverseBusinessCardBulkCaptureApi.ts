import {createBusinessCardBulkCaptureApi} from './businessCardBulkCapture'
import {dataverseBusinessCardCaptureInvoker} from './dataverseBusinessCardCaptureApi'

export const dataverseBusinessCardBulkCaptureApi = createBusinessCardBulkCaptureApi(dataverseBusinessCardCaptureInvoker)
